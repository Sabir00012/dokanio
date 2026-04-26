using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Core.Data;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;
using System.Text.Json;

// Disambiguate the two AppliedDiscount types
using DtoAppliedDiscount = Shared.Core.DTOs.AppliedDiscount;

namespace Shared.Core.Services;

/// <summary>
/// Wraps sale operations with comprehensive error handling, transaction rollback,
/// offline operation queuing, and automatic state persistence.
///
/// Requirements addressed:
///   8.1 - Clear error messages with recovery suggestions for database errors
///   8.3 - Log calculation errors and use safe fallback calculations
///   8.4 - Transaction rollback for failed operations
///   8.5 - Queue operations for later sync when network is unavailable
///   8.6 - Automatic state persistence to prevent data loss
/// </summary>
public class SaleErrorHandlingService : ISaleErrorHandlingService
{
    private readonly ISaleService _saleService;
    private readonly IOfflineQueueService _offlineQueueService;
    private readonly ITransactionStateService _transactionStateService;
    private readonly IConnectivityService _connectivityService;
    private readonly IGlobalExceptionHandler _globalExceptionHandler;
    private readonly PosDbContext _context;
    private readonly ILogger<SaleErrorHandlingService> _logger;

    public SaleErrorHandlingService(
        ISaleService saleService,
        IOfflineQueueService offlineQueueService,
        ITransactionStateService transactionStateService,
        IConnectivityService connectivityService,
        IGlobalExceptionHandler globalExceptionHandler,
        PosDbContext context,
        ILogger<SaleErrorHandlingService> logger)
    {
        _saleService = saleService ?? throw new ArgumentNullException(nameof(saleService));
        _offlineQueueService = offlineQueueService ?? throw new ArgumentNullException(nameof(offlineQueueService));
        _transactionStateService = transactionStateService ?? throw new ArgumentNullException(nameof(transactionStateService));
        _connectivityService = connectivityService ?? throw new ArgumentNullException(nameof(connectivityService));
        _globalExceptionHandler = globalExceptionHandler ?? throw new ArgumentNullException(nameof(globalExceptionHandler));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // Sale Operations with Error Handling
    // =========================================================================

    /// <inheritdoc/>
    public async Task<SaleOperationResult<Sale>> CreateSaleWithErrorHandlingAsync(
        string invoiceNumber,
        Guid deviceId,
        Guid? userId = null)
    {
        _logger.LogInformation(
            "Creating sale with error handling: invoice={InvoiceNumber}, device={DeviceId}",
            invoiceNumber, deviceId);

        try
        {
            var sale = await _saleService.CreateSaleAsync(invoiceNumber, deviceId);

            _logger.LogInformation(
                "Sale created successfully: {SaleId}, invoice={InvoiceNumber}",
                sale.Id, sale.InvoiceNumber);

            return SaleOperationResult<Sale>.Ok(sale);
        }
        catch (ArgumentException ex)
        {
            // Requirement 8.1: Clear error messages for validation failures
            _logger.LogWarning(ex, "Sale creation failed due to invalid input: {Message}", ex.Message);
            return SaleOperationResult<Sale>.Fail(
                $"Invalid input: {ex.Message}",
                "SALE_CREATE_INVALID_INPUT",
                "Check that the invoice number and device ID are valid.",
                "Ensure the device is registered in the system.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("License"))
        {
            _logger.LogWarning(ex, "Sale creation blocked by license check: {Message}", ex.Message);
            return SaleOperationResult<Sale>.Fail(
                "Cannot create sale: your license is not active.",
                "SALE_CREATE_LICENSE_INACTIVE",
                "Contact your administrator to activate the license.",
                "Check the license status in the configuration screen.");
        }
        catch (DbUpdateException ex)
        {
            // Requirement 8.1: Database errors with recovery suggestions
            _logger.LogError(ex, "Database error creating sale for device {DeviceId}", deviceId);
            await _globalExceptionHandler.LogExceptionAsync(ex, "SaleErrorHandlingService.CreateSale", deviceId, userId);

            return SaleOperationResult<Sale>.Fail(
                "Unable to save the new sale. Please try again.",
                "SALE_CREATE_DB_ERROR",
                "Try creating the sale again.",
                "If the problem persists, restart the application.",
                "Check available disk space.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating sale for device {DeviceId}", deviceId);
            await _globalExceptionHandler.LogExceptionAsync(ex, "SaleErrorHandlingService.CreateSale", deviceId, userId);

            return SaleOperationResult<Sale>.Fail(
                "An unexpected error occurred while creating the sale.",
                "SALE_CREATE_UNEXPECTED",
                "Try again in a few moments.",
                "If the problem persists, contact support.");
        }
    }

    /// <inheritdoc/>
    public async Task<SaleOperationResult<Sale>> AddItemWithErrorHandlingAsync(
        Guid saleId,
        Guid productId,
        int quantity,
        decimal unitPrice,
        string? batchNumber = null)
    {
        _logger.LogInformation(
            "Adding item to sale {SaleId}: product={ProductId}, qty={Quantity}",
            saleId, productId, quantity);

        // Requirement 8.4: Use EF Core transaction for rollback support
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var sale = await _saleService.AddItemToSaleAsync(saleId, productId, quantity, unitPrice, batchNumber);

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Item added successfully to sale {SaleId}: product={ProductId}",
                saleId, productId);

            return SaleOperationResult<Sale>.Ok(sale);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("stock") || ex.Message.Contains("Stock"))
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Insufficient stock for product {ProductId} in sale {SaleId}", productId, saleId);

            return SaleOperationResult<Sale>.Fail(
                $"Cannot add product: {ex.Message}",
                "SALE_ADD_ITEM_INSUFFICIENT_STOCK",
                "Check the available stock for this product.",
                "Try a smaller quantity.",
                "Contact your supplier to restock.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("expired") || ex.Message.Contains("inactive"))
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Product {ProductId} is not valid for sale", productId);

            return SaleOperationResult<Sale>.Fail(
                $"Cannot add product: {ex.Message}",
                "SALE_ADD_ITEM_PRODUCT_INVALID",
                "Select a different product.",
                "Check the product's expiry date and active status.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("status"))
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Cannot add item to sale {SaleId} in current status", saleId);

            return SaleOperationResult<Sale>.Fail(
                $"Cannot modify this sale: {ex.Message}",
                "SALE_ADD_ITEM_WRONG_STATUS",
                "The sale may already be completed or cancelled.",
                "Start a new sale if needed.");
        }
        catch (DbUpdateException ex)
        {
            // Requirement 8.4: Rollback on database failure
            await RollbackSafelyAsync(transaction, saleId, "AddItem");
            _logger.LogError(ex, "Database error adding item to sale {SaleId}", saleId);

            return SaleOperationResult<Sale>.Fail(
                "Unable to save the item. The change has been rolled back.",
                "SALE_ADD_ITEM_DB_ERROR",
                "Try adding the item again.",
                "If the problem persists, restart the application.");
        }
        catch (Exception ex)
        {
            await RollbackSafelyAsync(transaction, saleId, "AddItem");
            _logger.LogError(ex, "Unexpected error adding item to sale {SaleId}", saleId);

            return SaleOperationResult<Sale>.Fail(
                "An unexpected error occurred while adding the item. The change has been rolled back.",
                "SALE_ADD_ITEM_UNEXPECTED",
                "Try again in a few moments.",
                "If the problem persists, contact support.");
        }
    }

    /// <inheritdoc/>
    public async Task<SaleOperationResult<Sale>> CompleteSaleWithErrorHandlingAsync(
        Guid saleId,
        PaymentMethod paymentMethod,
        Guid deviceId)
    {
        _logger.LogInformation(
            "Completing sale {SaleId} with payment method {PaymentMethod} on device {DeviceId}",
            saleId, paymentMethod, deviceId);

        // Requirement 8.4: Use EF Core transaction for rollback support
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var sale = await _saleService.CompleteSaleAsync(saleId, paymentMethod);

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Sale {SaleId} completed successfully, total={Total}",
                saleId, sale.TotalAmount);

            // Requirement 8.5: Queue for server sync (offline-first: local write already done)
            await QueueSaleForSyncAsync(sale, deviceId);

            return SaleOperationResult<Sale>.Ok(sale);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already completed"))
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Sale {SaleId} is already completed", saleId);

            return SaleOperationResult<Sale>.Fail(
                "This sale has already been completed.",
                "SALE_COMPLETE_ALREADY_DONE",
                "Check the sale status before completing.",
                "If you need to process a refund, use the refund function.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no items"))
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Cannot complete sale {SaleId}: no items", saleId);

            return SaleOperationResult<Sale>.Fail(
                "Cannot complete a sale with no items.",
                "SALE_COMPLETE_NO_ITEMS",
                "Add at least one product before completing the sale.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("License"))
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Sale completion blocked by license check for sale {SaleId}", saleId);

            return SaleOperationResult<Sale>.Fail(
                "Cannot complete sale: your license is not active.",
                "SALE_COMPLETE_LICENSE_INACTIVE",
                "Contact your administrator to activate the license.");
        }
        catch (DbUpdateException ex)
        {
            // Requirement 8.4: Rollback and preserve state for retry
            await RollbackSafelyAsync(transaction, saleId, "CompleteSale");
            _logger.LogError(ex, "Database error completing sale {SaleId}", saleId);

            // Requirement 8.5: Queue for later processing
            var sale = await _saleService.GetSaleByIdAsync(saleId);
            if (sale != null)
            {
                var userId = sale.UserId;
                var shopId = sale.ShopId;
                await QueueSaleCompletionAsync(saleId, paymentMethod, deviceId, userId, shopId);

                return new SaleOperationResult<Sale>
                {
                    Success = false,
                    IsQueued = true,
                    RolledBack = true,
                    ErrorMessage = "Unable to complete the sale right now. The operation has been queued and will be processed when connectivity is restored.",
                    ErrorCode = "SALE_COMPLETE_DB_ERROR_QUEUED",
                    RecoverySuggestions = new List<string>
                    {
                        "The sale completion has been queued for retry.",
                        "Check your network connection.",
                        "The sale will complete automatically when connectivity is restored."
                    }
                };
            }

            return SaleOperationResult<Sale>.Fail(
                "Unable to complete the sale. The change has been rolled back.",
                "SALE_COMPLETE_DB_ERROR",
                "Try completing the sale again.",
                "If the problem persists, restart the application.");
        }
        catch (Exception ex)
        {
            await RollbackSafelyAsync(transaction, saleId, "CompleteSale");
            _logger.LogError(ex, "Unexpected error completing sale {SaleId}", saleId);

            return SaleOperationResult<Sale>.Fail(
                "An unexpected error occurred while completing the sale. The change has been rolled back.",
                "SALE_COMPLETE_UNEXPECTED",
                "Try again in a few moments.",
                "If the problem persists, contact support.");
        }
    }

    /// <inheritdoc/>
    public async Task<SaleOperationResult<Sale>> CancelSaleWithErrorHandlingAsync(
        Guid saleId,
        string reason,
        Guid deviceId)
    {
        _logger.LogInformation("Cancelling sale {SaleId} on device {DeviceId}", saleId, deviceId);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var sale = await _saleService.CancelSaleAsync(saleId, reason);

            await transaction.CommitAsync();

            _logger.LogInformation("Sale {SaleId} cancelled successfully", saleId);

            return SaleOperationResult<Sale>.Ok(sale);
        }
        catch (ArgumentException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Invalid input cancelling sale {SaleId}: {Message}", saleId, ex.Message);

            return SaleOperationResult<Sale>.Fail(
                $"Cannot cancel sale: {ex.Message}",
                "SALE_CANCEL_INVALID_INPUT",
                "Provide a reason for the cancellation.",
                "Ensure the sale ID is correct.");
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Cannot cancel sale {SaleId}: {Message}", saleId, ex.Message);

            return SaleOperationResult<Sale>.Fail(
                $"Cannot cancel this sale: {ex.Message}",
                "SALE_CANCEL_INVALID_STATE",
                "Check the current status of the sale.",
                "Completed sales cannot be cancelled — use the refund function instead.");
        }
        catch (DbUpdateException ex)
        {
            await RollbackSafelyAsync(transaction, saleId, "CancelSale");
            _logger.LogError(ex, "Database error cancelling sale {SaleId}", saleId);

            return SaleOperationResult<Sale>.Fail(
                "Unable to cancel the sale. The change has been rolled back.",
                "SALE_CANCEL_DB_ERROR",
                "Try cancelling the sale again.",
                "If the problem persists, restart the application.");
        }
        catch (Exception ex)
        {
            await RollbackSafelyAsync(transaction, saleId, "CancelSale");
            _logger.LogError(ex, "Unexpected error cancelling sale {SaleId}", saleId);

            return SaleOperationResult<Sale>.Fail(
                "An unexpected error occurred while cancelling the sale.",
                "SALE_CANCEL_UNEXPECTED",
                "Try again in a few moments.",
                "If the problem persists, contact support.");
        }
    }

    // =========================================================================
    // Calculation Error Handling
    // =========================================================================

    /// <inheritdoc/>
    public async Task<SaleOperationResult<SaleCalculationResult>> RecalculateWithFallbackAsync(Guid saleId)
    {
        _logger.LogDebug("Recalculating sale totals for sale {SaleId}", saleId);

        try
        {
            var result = await _saleService.CalculateFullSaleTotalAsync(saleId);
            return SaleOperationResult<SaleCalculationResult>.Ok(result);
        }
        catch (DivideByZeroException ex)
        {
            // Requirement 8.3: Log and use safe fallback
            _logger.LogError(ex,
                "Division by zero in calculation for sale {SaleId}. Using safe fallback (zero discount/tax).",
                saleId);

            var fallback = await BuildFallbackCalculationAsync(saleId);
            return new SaleOperationResult<SaleCalculationResult>
            {
                Success = true,
                Value = fallback,
                ErrorMessage = "A calculation error occurred. Totals have been recalculated using safe defaults (no discounts or taxes applied). Please verify the amounts.",
                ErrorCode = "CALC_DIVISION_BY_ZERO_FALLBACK"
            };
        }
        catch (OverflowException ex)
        {
            // Requirement 8.3: Log and use safe fallback
            _logger.LogError(ex,
                "Overflow in calculation for sale {SaleId}. Using safe fallback.",
                saleId);

            var fallback = await BuildFallbackCalculationAsync(saleId);
            return new SaleOperationResult<SaleCalculationResult>
            {
                Success = true,
                Value = fallback,
                ErrorMessage = "A calculation overflow occurred. Totals have been recalculated using safe defaults. Please verify the amounts.",
                ErrorCode = "CALC_OVERFLOW_FALLBACK"
            };
        }
        catch (Exception ex)
        {
            // Requirement 8.3: Log the error and use safe fallback
            _logger.LogError(ex,
                "Calculation error for sale {SaleId}. Using safe fallback (base total only).",
                saleId);

            var fallback = await BuildFallbackCalculationAsync(saleId);
            return new SaleOperationResult<SaleCalculationResult>
            {
                Success = true,
                Value = fallback,
                ErrorMessage = "A calculation error occurred. Totals have been recalculated using safe defaults. Please verify the amounts before completing the sale.",
                ErrorCode = "CALC_ERROR_FALLBACK"
            };
        }
    }

    // =========================================================================
    // State Persistence
    // =========================================================================

    /// <inheritdoc/>
    public async Task<bool> PersistSaleStateAsync(Guid saleId, Guid sessionId, Guid deviceId, Guid userId)
    {
        _logger.LogDebug("Persisting state for sale {SaleId}, session {SessionId}", saleId, sessionId);

        try
        {
            var sale = await _saleService.GetSaleByIdAsync(saleId);
            if (sale == null)
            {
                _logger.LogWarning("Cannot persist state: sale {SaleId} not found", saleId);
                return false;
            }

            // Ensure a SaleSession record exists for the TransactionStateService to update
            await EnsureSaleSessionExistsAsync(sessionId, saleId, deviceId, userId, sale.ShopId);

            var transactionState = BuildTransactionState(sale, sessionId, deviceId, userId);
            var saved = await _transactionStateService.SaveTransactionStateAsync(sessionId, transactionState);

            if (saved)
            {
                _logger.LogDebug("State persisted for sale {SaleId}", saleId);
            }
            else
            {
                _logger.LogWarning("Failed to persist state for sale {SaleId}", saleId);
            }

            return saved;
        }
        catch (Exception ex)
        {
            // Requirement 8.6: State persistence failures are non-fatal — log but don't throw
            _logger.LogError(ex, "Error persisting state for sale {SaleId}", saleId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<SaleOperationResult<Sale>> RestoreSaleStateAsync(Guid sessionId)
    {
        _logger.LogInformation("Restoring sale state for session {SessionId}", sessionId);

        try
        {
            var state = await _transactionStateService.RestoreTransactionStateAsync(sessionId);
            if (state == null)
            {
                return SaleOperationResult<Sale>.Fail(
                    "No saved state found for this session.",
                    "RESTORE_STATE_NOT_FOUND",
                    "The session may have expired or was never saved.",
                    "Start a new sale.");
            }

            // Attempt to retrieve the actual sale from the database
            var sale = await _saleService.GetSaleByIdAsync(state.SaleSessionId);
            if (sale == null)
            {
                return SaleOperationResult<Sale>.Fail(
                    "The sale associated with this session could not be found.",
                    "RESTORE_SALE_NOT_FOUND",
                    "The sale may have been deleted.",
                    "Start a new sale.");
            }

            _logger.LogInformation(
                "Restored sale state for session {SessionId}: sale {SaleId}",
                sessionId, sale.Id);

            return SaleOperationResult<Sale>.Ok(sale);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring sale state for session {SessionId}", sessionId);

            return SaleOperationResult<Sale>.Fail(
                "Unable to restore the previous sale state.",
                "RESTORE_STATE_ERROR",
                "Start a new sale.",
                "If you had items in progress, you may need to re-add them.");
        }
    }

    // =========================================================================
    // Offline Queue Integration
    // =========================================================================

    /// <inheritdoc/>
    public async Task<bool> QueueSaleCompletionAsync(
        Guid saleId,
        PaymentMethod paymentMethod,
        Guid deviceId,
        Guid userId,
        Guid shopId)
    {
        _logger.LogInformation(
            "Queuing sale completion for offline processing: sale {SaleId}, device {DeviceId}",
            saleId, deviceId);

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                SaleId = saleId,
                PaymentMethod = paymentMethod,
                QueuedAt = DateTime.UtcNow
            });

            var operation = new OfflineOperation
            {
                OperationType = "CompleteSale",
                EntityType = "Sale",
                EntityId = saleId,
                SerializedData = payload,
                Priority = OperationPriority.Critical, // Sale completions are critical
                UserId = userId,
                DeviceId = deviceId,
                ShopId = shopId
            };

            var queued = await _offlineQueueService.QueueOperationAsync(operation);

            if (queued)
            {
                _logger.LogInformation(
                    "Sale completion queued for offline processing: sale {SaleId}",
                    saleId);
            }

            return queued;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queuing sale completion for sale {SaleId}", saleId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<int> GetPendingOfflineOperationCountAsync(Guid deviceId)
    {
        try
        {
            var stats = await _offlineQueueService.GetQueueStatisticsAsync();
            return stats.PendingOperations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending offline operation count for device {DeviceId}", deviceId);
            return 0;
        }
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// Safely rolls back a transaction, logging any rollback errors without rethrowing.
    /// Requirement 8.4: Ensures rollback is always attempted on failure.
    /// </summary>
    private async Task RollbackSafelyAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Guid saleId,
        string operationName)
    {
        try
        {
            await transaction.RollbackAsync();
            _logger.LogInformation(
                "Transaction rolled back for {OperationName} on sale {SaleId}",
                operationName, saleId);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx,
                "Error rolling back transaction for {OperationName} on sale {SaleId}",
                operationName, saleId);
        }
    }

    /// <summary>
    /// Builds a safe fallback calculation result using only the base item totals.
    /// Requirement 8.3: Safe fallback when calculation errors occur.
    /// </summary>
    private async Task<SaleCalculationResult> BuildFallbackCalculationAsync(Guid saleId)
    {
        try
        {
            // Use the simple total (no discounts, no taxes) as a safe fallback
            var baseTotal = await _saleService.CalculateSaleTotalAsync(saleId);

            _logger.LogWarning(
                "Using fallback calculation for sale {SaleId}: base total = {BaseTotal}, no discounts/taxes applied",
                saleId, baseTotal);

            return new SaleCalculationResult
            {
                BaseTotal = baseTotal,
                DiscountAmount = 0,
                MembershipDiscountAmount = 0,
                TaxAmount = 0,
                FinalTotal = baseTotal,
                AppliedDiscounts = new List<DtoAppliedDiscount>(),
                DiscountReasons = new List<string> { "Fallback calculation: discounts and taxes could not be applied due to a calculation error." }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Even fallback calculation failed for sale {SaleId}", saleId);

            // Last resort: return zero totals
            return new SaleCalculationResult
            {
                BaseTotal = 0,
                DiscountAmount = 0,
                MembershipDiscountAmount = 0,
                TaxAmount = 0,
                FinalTotal = 0,
                DiscountReasons = new List<string> { "Calculation unavailable. Please verify totals manually." }
            };
        }
    }

    /// <summary>
    /// Ensures a SaleSession record exists for the given session ID.
    /// Creates one if it doesn't exist, so TransactionStateService can persist state.
    /// </summary>
    private async Task EnsureSaleSessionExistsAsync(
        Guid sessionId,
        Guid saleId,
        Guid deviceId,
        Guid userId,
        Guid shopId)
    {
        var existing = await _context.SaleSessions
            .FirstOrDefaultAsync(ss => ss.Id == sessionId && !ss.IsDeleted);

        if (existing == null)
        {
            var session = new SaleSession
            {
                Id = sessionId,
                TabName = $"Sale-{saleId:N}".Substring(0, Math.Min(100, $"Sale-{saleId:N}".Length)),
                ShopId = shopId,
                UserId = userId,
                DeviceId = deviceId,
                SaleId = saleId,
                State = SessionState.Active,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };

            _context.SaleSessions.Add(session);
            await _context.SaveChangesAsync();

            _logger.LogDebug(
                "Created SaleSession {SessionId} for sale {SaleId} to support state persistence",
                sessionId, saleId);
        }
    }

    /// <summary>
    /// Builds a TransactionState from a Sale entity for persistence.
    /// </summary>
    private static TransactionState BuildTransactionState(Sale sale, Guid sessionId, Guid deviceId, Guid userId)
    {
        return new TransactionState
        {
            SaleSessionId = sessionId,
            UserId = userId,
            DeviceId = deviceId,
            ShopId = sale.ShopId,
            CustomerId = sale.CustomerId,
            CustomerName = sale.Customer?.Name,
            CustomerMobileNumber = sale.Customer?.Phone,
            PaymentMethod = sale.PaymentMethod,
            Subtotal = sale.TotalAmount,
            TotalDiscount = sale.DiscountAmount + sale.MembershipDiscountAmount,
            TotalTax = sale.TaxAmount,
            FinalTotal = sale.TotalAmount,
            SaleItems = sale.Items
                .Where(i => !i.IsDeleted)
                .Select(i => new TransactionSaleItem
                {
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name ?? string.Empty,
                    Quantity = i.Quantity,
                    Weight = i.Weight,
                    UnitPrice = i.UnitPrice,
                    LineTotal = i.TotalPrice,
                    IsWeightBased = i.IsWeightBased,
                    BatchNumber = i.BatchNumber
                })
                .ToList(),
            LastSavedAt = DateTime.UtcNow,
            IsCompleted = sale.Status == SaleStatus.Completed
        };
    }

    /// <summary>
    /// Queues a completed sale for server synchronization.
    /// Requirement 8.5: Offline-first — local write is done, queue for server sync.
    /// </summary>
    private async Task QueueSaleForSyncAsync(Sale sale, Guid deviceId)
    {
        try
        {
            await _offlineQueueService.QueueSaleAsync(sale, OperationPriority.High);
            _logger.LogDebug("Sale {SaleId} queued for server sync", sale.Id);
        }
        catch (Exception ex)
        {
            // Non-fatal: local write succeeded, sync will be retried by SyncEngine
            _logger.LogWarning(ex, "Could not queue sale {SaleId} for sync (will be picked up by SyncEngine)", sale.Id);
        }
    }
}
