using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;

namespace Shared.Core.Services;

/// <summary>
/// Service that wraps sale operations with comprehensive error handling, transaction rollback,
/// offline operation queuing, and automatic state persistence.
/// Addresses Requirements 8.1, 8.3, 8.4, 8.5.
/// </summary>
public interface ISaleErrorHandlingService
{
    // =========================================================================
    // Sale Operations with Error Handling
    // =========================================================================

    /// <summary>
    /// Creates a sale with full error handling and state persistence.
    /// Requirement 8.1: Provides clear error messages and recovery suggestions on failure.
    /// Requirement 8.5: Queues the operation if network is unavailable.
    /// </summary>
    Task<SaleOperationResult<Sale>> CreateSaleWithErrorHandlingAsync(
        string invoiceNumber,
        Guid deviceId,
        Guid? userId = null);

    /// <summary>
    /// Adds an item to a sale with error handling and automatic state persistence.
    /// Requirement 8.4: Rolls back on failure to maintain data consistency.
    /// Requirement 8.6: Persists state automatically after each modification.
    /// </summary>
    Task<SaleOperationResult<Sale>> AddItemWithErrorHandlingAsync(
        Guid saleId,
        Guid productId,
        int quantity,
        decimal unitPrice,
        string? batchNumber = null);

    /// <summary>
    /// Completes a sale with error handling, rollback on failure, and state preservation.
    /// Requirement 8.4: Rolls back all changes if completion fails.
    /// Requirement 8.5: Queues for later sync if network is unavailable.
    /// </summary>
    Task<SaleOperationResult<Sale>> CompleteSaleWithErrorHandlingAsync(
        Guid saleId,
        PaymentMethod paymentMethod,
        Guid deviceId);

    /// <summary>
    /// Cancels a sale with error handling and state persistence.
    /// Requirement 8.1: Provides clear error messages on failure.
    /// </summary>
    Task<SaleOperationResult<Sale>> CancelSaleWithErrorHandlingAsync(
        Guid saleId,
        string reason,
        Guid deviceId);

    // =========================================================================
    // Calculation Error Handling
    // =========================================================================

    /// <summary>
    /// Recalculates sale totals with safe fallback on calculation errors.
    /// Requirement 8.3: Logs calculation errors and uses safe fallback calculations.
    /// </summary>
    Task<SaleOperationResult<SaleCalculationResult>> RecalculateWithFallbackAsync(Guid saleId);

    // =========================================================================
    // State Persistence
    // =========================================================================

    /// <summary>
    /// Persists the current state of an active sale session.
    /// Requirement 8.6: Automatically saves sale state to prevent data loss.
    /// </summary>
    Task<bool> PersistSaleStateAsync(Guid saleId, Guid sessionId, Guid deviceId, Guid userId);

    /// <summary>
    /// Restores a previously persisted sale state.
    /// Requirement 8.6: Restores sale state after failures.
    /// </summary>
    Task<SaleOperationResult<Sale>> RestoreSaleStateAsync(Guid sessionId);

    // =========================================================================
    // Offline Queue Integration
    // =========================================================================

    /// <summary>
    /// Queues a sale completion for later sync when network is unavailable.
    /// Requirement 8.5: Queues operations for later synchronization when offline.
    /// </summary>
    Task<bool> QueueSaleCompletionAsync(Guid saleId, PaymentMethod paymentMethod, Guid deviceId, Guid userId, Guid shopId);

    /// <summary>
    /// Gets the count of pending offline operations for a device.
    /// </summary>
    Task<int> GetPendingOfflineOperationCountAsync(Guid deviceId);
}

/// <summary>
/// Result of a sale operation that includes success status, error details, and recovery suggestions.
/// </summary>
public class SaleOperationResult<T>
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>The result value when the operation succeeds.</summary>
    public T? Value { get; set; }

    /// <summary>User-friendly error message when the operation fails.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Technical error code for logging and diagnostics.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Suggested recovery actions for the user.</summary>
    public List<string> RecoverySuggestions { get; set; } = new();

    /// <summary>Whether the operation was queued for offline processing.</summary>
    public bool IsQueued { get; set; }

    /// <summary>Whether the state was persisted for recovery.</summary>
    public bool StatePersisted { get; set; }

    /// <summary>Whether a rollback was performed.</summary>
    public bool RolledBack { get; set; }

    /// <summary>Creates a successful result.</summary>
    public static SaleOperationResult<T> Ok(T value) => new()
    {
        Success = true,
        Value = value
    };

    /// <summary>Creates a failed result with a user-friendly message.</summary>
    public static SaleOperationResult<T> Fail(string errorMessage, string? errorCode = null, params string[] recoverySuggestions) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        ErrorCode = errorCode,
        RecoverySuggestions = recoverySuggestions.ToList()
    };

    /// <summary>Creates a queued result (offline mode).</summary>
    public static SaleOperationResult<T> Queued(string message) => new()
    {
        Success = true,
        IsQueued = true,
        ErrorMessage = message
    };
}
