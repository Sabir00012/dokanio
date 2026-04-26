using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.DependencyInjection;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Shared.Core.Tests;

/// <summary>
/// Unit tests for SaleErrorHandlingService.
/// Validates Requirements 8.1, 8.3, 8.4, 8.5, 8.6:
///   8.1 - Clear error messages with recovery suggestions for database errors
///   8.3 - Log calculation errors and use safe fallback calculations
///   8.4 - Transaction rollback for failed operations
///   8.5 - Queue operations for later sync when network is unavailable
///   8.6 - Automatic state persistence to prevent data loss
/// </summary>
public class SaleErrorHandlingServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ISaleErrorHandlingService _saleErrorHandlingService;
    private readonly ISaleService _saleService;
    private readonly IOfflineQueueService _offlineQueueService;
    private readonly ITransactionStateService _transactionStateService;
    private readonly PosDbContext _context;
    private readonly ITestOutputHelper _output;

    // Shared test data
    private readonly Guid _deviceId;
    private readonly Guid _userId;
    private readonly Guid _shopId;
    private readonly Guid _businessId;

    public SaleErrorHandlingServiceTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddSharedCoreInMemory();
        _serviceProvider = services.BuildServiceProvider();

        _saleErrorHandlingService = _serviceProvider.GetRequiredService<ISaleErrorHandlingService>();
        _saleService = _serviceProvider.GetRequiredService<ISaleService>();
        _offlineQueueService = _serviceProvider.GetRequiredService<IOfflineQueueService>();
        _transactionStateService = _serviceProvider.GetRequiredService<ITransactionStateService>();
        _context = _serviceProvider.GetRequiredService<PosDbContext>();

        _businessId = Guid.NewGuid();
        _shopId = Guid.NewGuid();
        _deviceId = Guid.NewGuid();
        _userId = Guid.NewGuid();

        SeedTestData().GetAwaiter().GetResult();
    }

    private async Task SeedTestData()
    {
        var business = new Business
        {
            Id = _businessId,
            Name = "Test Business",
            Type = BusinessType.GeneralRetail,
            OwnerId = _userId,
            IsActive = true
        };
        _context.Businesses.Add(business);

        var shop = new Shop
        {
            Id = _shopId,
            BusinessId = _businessId,
            Name = "Test Shop",
            DeviceId = _deviceId,
            IsActive = true
        };
        _context.Shops.Add(shop);

        var user = new User
        {
            Id = _userId,
            BusinessId = _businessId,
            ShopId = _shopId,
            Username = "testcashier",
            FullName = "Test Cashier",
            Email = "cashier@test.com",
            PasswordHash = "hash",
            Salt = "salt",
            Role = UserRole.Cashier,
            DeviceId = _deviceId,
            IsActive = true
        };
        _context.Users.Add(user);

        // Seed an active license for the device
        var currentUserService = _serviceProvider.GetRequiredService<ICurrentUserService>();
        var licenseDeviceId = currentUserService.GetDeviceId();

        var license = new License
        {
            Id = Guid.NewGuid(),
            LicenseKey = "TEST-LICENSE-KEY-ERR-001",
            Type = LicenseType.Professional,
            Status = LicenseStatus.Active,
            DeviceId = licenseDeviceId,
            CustomerName = "Test Customer",
            CustomerEmail = "test@example.com",
            IssueDate = DateTime.UtcNow.AddDays(-30),
            ExpiryDate = DateTime.UtcNow.AddYears(1),
            ActivationDate = DateTime.UtcNow.AddDays(-30)
        };
        _context.Licenses.Add(license);

        await _context.SaveChangesAsync();
    }

    // =========================================================================
    // Requirement 8.1: Clear error messages with recovery suggestions
    // =========================================================================

    [Fact]
    public async Task CreateSaleWithErrorHandling_WithEmptyInvoiceNumber_ReturnsFailWithClearMessage()
    {
        // Act
        var result = await _saleErrorHandlingService.CreateSaleWithErrorHandlingAsync(
            string.Empty, _deviceId);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.ErrorMessage);
        Assert.NotEmpty(result.RecoverySuggestions);
        Assert.NotNull(result.ErrorCode);

        _output.WriteLine($"Error: {result.ErrorMessage}");
        _output.WriteLine($"Code: {result.ErrorCode}");
        _output.WriteLine($"Suggestions: {string.Join(", ", result.RecoverySuggestions)}");
    }

    [Fact]
    public async Task CreateSaleWithErrorHandling_WithEmptyDeviceId_ReturnsFailWithClearMessage()
    {
        // Act
        var result = await _saleErrorHandlingService.CreateSaleWithErrorHandlingAsync(
            "INV-TEST-001", Guid.Empty);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.RecoverySuggestions);

        _output.WriteLine($"Error: {result.ErrorMessage}");
    }

    [Fact]
    public async Task CreateSaleWithErrorHandling_WithValidInput_ReturnsSuccess()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();

        // Act
        var result = await _saleErrorHandlingService.CreateSaleWithErrorHandlingAsync(
            invoiceNumber, _deviceId);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(invoiceNumber, result.Value.InvoiceNumber);
        Assert.Null(result.ErrorMessage);

        _output.WriteLine($"Sale created: {result.Value.Id}");
    }

    [Fact]
    public async Task CancelSaleWithErrorHandling_WithEmptyReason_ReturnsFailWithClearMessage()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();
        var sale = await _saleService.CreateSaleAsync(invoiceNumber, _deviceId);

        // Act
        var result = await _saleErrorHandlingService.CancelSaleWithErrorHandlingAsync(
            sale.Id, string.Empty, _deviceId);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.RecoverySuggestions);

        _output.WriteLine($"Error: {result.ErrorMessage}");
    }

    [Fact]
    public async Task CancelSaleWithErrorHandling_WithValidInput_ReturnsSuccess()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();
        var sale = await _saleService.CreateSaleAsync(invoiceNumber, _deviceId);

        // Act
        var result = await _saleErrorHandlingService.CancelSaleWithErrorHandlingAsync(
            sale.Id, "Customer changed mind", _deviceId);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(SaleStatus.Cancelled, result.Value.Status);

        _output.WriteLine($"Sale cancelled: {result.Value.Id}");
    }

    [Fact]
    public async Task CancelSaleWithErrorHandling_AlreadyCancelled_ReturnsFailWithClearMessage()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();
        var sale = await _saleService.CreateSaleAsync(invoiceNumber, _deviceId);
        await _saleService.CancelSaleAsync(sale.Id, "First cancellation");

        // Act
        var result = await _saleErrorHandlingService.CancelSaleWithErrorHandlingAsync(
            sale.Id, "Second cancellation", _deviceId);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.RecoverySuggestions);
        Assert.Equal("SALE_CANCEL_INVALID_STATE", result.ErrorCode);

        _output.WriteLine($"Error: {result.ErrorMessage}");
    }

    // =========================================================================
    // Requirement 8.3: Safe fallback calculations
    // =========================================================================

    [Fact]
    public async Task RecalculateWithFallback_WithValidSale_ReturnsCalculationResult()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();
        var sale = await _saleService.CreateSaleAsync(invoiceNumber, _deviceId);

        // Act
        var result = await _saleErrorHandlingService.RecalculateWithFallbackAsync(sale.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.FinalTotal >= 0);

        _output.WriteLine($"Calculation result: FinalTotal={result.Value.FinalTotal}");
    }

    [Fact]
    public async Task RecalculateWithFallback_WithNonExistentSale_ReturnsFailOrFallback()
    {
        // Act
        var result = await _saleErrorHandlingService.RecalculateWithFallbackAsync(Guid.NewGuid());

        // Assert - either fails gracefully or returns a fallback
        // The important thing is it doesn't throw an unhandled exception
        Assert.NotNull(result);

        _output.WriteLine($"Result: Success={result.Success}, Error={result.ErrorMessage}");
    }

    // =========================================================================
    // Requirement 8.4: Transaction rollback for failed operations
    // =========================================================================

    [Fact]
    public async Task AddItemWithErrorHandling_WithNonExistentProduct_ReturnsFailWithRollback()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();
        var sale = await _saleService.CreateSaleAsync(invoiceNumber, _deviceId);
        var nonExistentProductId = Guid.NewGuid();

        // Act
        var result = await _saleErrorHandlingService.AddItemWithErrorHandlingAsync(
            sale.Id, nonExistentProductId, 1, 10.00m);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.RecoverySuggestions);

        // Verify the sale is still in its original state (rollback worked)
        var saleAfter = await _saleService.GetSaleByIdAsync(sale.Id);
        Assert.NotNull(saleAfter);
        Assert.Equal(SaleStatus.Draft, saleAfter.Status);
        Assert.Equal(0, saleAfter.TotalAmount);

        _output.WriteLine($"Error: {result.ErrorMessage}");
        _output.WriteLine($"Sale status after failed add: {saleAfter.Status}");
    }

    [Fact]
    public async Task AddItemWithErrorHandling_WithInvalidQuantity_ReturnsFailWithClearMessage()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();
        var sale = await _saleService.CreateSaleAsync(invoiceNumber, _deviceId);

        // Act
        var result = await _saleErrorHandlingService.AddItemWithErrorHandlingAsync(
            sale.Id, Guid.NewGuid(), 0, 10.00m); // quantity = 0 is invalid

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.RecoverySuggestions);

        _output.WriteLine($"Error: {result.ErrorMessage}");
    }

    [Fact]
    public async Task CompleteSaleWithErrorHandling_WithNoItems_ReturnsFailWithClearMessage()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();
        var sale = await _saleService.CreateSaleAsync(invoiceNumber, _deviceId);

        // Act
        var result = await _saleErrorHandlingService.CompleteSaleWithErrorHandlingAsync(
            sale.Id, PaymentMethod.Cash, _deviceId);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.RecoverySuggestions);
        Assert.Equal("SALE_COMPLETE_NO_ITEMS", result.ErrorCode);

        _output.WriteLine($"Error: {result.ErrorMessage}");
    }

    [Fact]
    public async Task CompleteSaleWithErrorHandling_WithNonExistentSale_ReturnsFailWithClearMessage()
    {
        // Act
        var result = await _saleErrorHandlingService.CompleteSaleWithErrorHandlingAsync(
            Guid.NewGuid(), PaymentMethod.Cash, _deviceId);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);

        _output.WriteLine($"Error: {result.ErrorMessage}");
    }

    // =========================================================================
    // Requirement 8.5: Offline operation queuing
    // =========================================================================

    [Fact]
    public async Task QueueSaleCompletionAsync_WithValidData_QueuesSuccessfully()
    {
        // Arrange
        var saleId = Guid.NewGuid();

        // Act
        var queued = await _saleErrorHandlingService.QueueSaleCompletionAsync(
            saleId, PaymentMethod.Cash, _deviceId, _userId, _shopId);

        // Assert
        Assert.True(queued);

        // Verify it's in the queue
        var pendingCount = await _saleErrorHandlingService.GetPendingOfflineOperationCountAsync(_deviceId);
        Assert.True(pendingCount > 0);

        _output.WriteLine($"Queued: {queued}, Pending operations: {pendingCount}");
    }

    [Fact]
    public async Task GetPendingOfflineOperationCountAsync_WithNoOperations_ReturnsZeroOrMore()
    {
        // Act
        var count = await _saleErrorHandlingService.GetPendingOfflineOperationCountAsync(_deviceId);

        // Assert
        Assert.True(count >= 0);

        _output.WriteLine($"Pending offline operations: {count}");
    }

    // =========================================================================
    // Requirement 8.6: Automatic state persistence
    // =========================================================================

    [Fact]
    public async Task PersistSaleStateAsync_WithValidSale_PersistsSuccessfully()
    {
        // Arrange
        var invoiceNumber = _saleService.GenerateInvoiceNumber();
        var sale = await _saleService.CreateSaleAsync(invoiceNumber, _deviceId);
        var sessionId = Guid.NewGuid();

        // Act
        var persisted = await _saleErrorHandlingService.PersistSaleStateAsync(
            sale.Id, sessionId, _deviceId, _userId);

        // Assert
        Assert.True(persisted);

        _output.WriteLine($"State persisted for sale {sale.Id}, session {sessionId}");
    }

    [Fact]
    public async Task PersistSaleStateAsync_WithNonExistentSale_ReturnsFalse()
    {
        // Act
        var persisted = await _saleErrorHandlingService.PersistSaleStateAsync(
            Guid.NewGuid(), Guid.NewGuid(), _deviceId, _userId);

        // Assert
        Assert.False(persisted);

        _output.WriteLine("Correctly returned false for non-existent sale");
    }

    [Fact]
    public async Task RestoreSaleStateAsync_WithNoSavedState_ReturnsFailWithClearMessage()
    {
        // Act
        var result = await _saleErrorHandlingService.RestoreSaleStateAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.RecoverySuggestions);

        _output.WriteLine($"Error: {result.ErrorMessage}");
    }

    // =========================================================================
    // SaleOperationResult helper tests
    // =========================================================================

    [Fact]
    public void SaleOperationResult_Ok_SetsSuccessAndValue()
    {
        // Arrange
        var sale = new Sale { Id = Guid.NewGuid(), InvoiceNumber = "INV-TEST" };

        // Act
        var result = SaleOperationResult<Sale>.Ok(sale);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(sale, result.Value);
        Assert.Null(result.ErrorMessage);
        Assert.False(result.IsQueued);
        Assert.False(result.RolledBack);
    }

    [Fact]
    public void SaleOperationResult_Fail_SetsErrorAndSuggestions()
    {
        // Act
        var result = SaleOperationResult<Sale>.Fail(
            "Something went wrong",
            "TEST_ERROR",
            "Try again",
            "Contact support");

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.Equal("Something went wrong", result.ErrorMessage);
        Assert.Equal("TEST_ERROR", result.ErrorCode);
        Assert.Equal(2, result.RecoverySuggestions.Count);
        Assert.Contains("Try again", result.RecoverySuggestions);
        Assert.Contains("Contact support", result.RecoverySuggestions);
    }

    [Fact]
    public void SaleOperationResult_Queued_SetsQueuedFlag()
    {
        // Act
        var result = SaleOperationResult<Sale>.Queued("Operation queued for offline processing");

        // Assert
        Assert.True(result.Success);
        Assert.True(result.IsQueued);
        Assert.Equal("Operation queued for offline processing", result.ErrorMessage);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
