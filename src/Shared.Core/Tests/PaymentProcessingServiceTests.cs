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
/// Unit tests for PaymentProcessingService.
/// Covers: payment method validation (Req 6.1), final total calculation (Req 6.2),
/// receipt generation (Req 6.4), and payment failure state preservation (Req 6.5).
/// </summary>
public class PaymentProcessingServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IPaymentProcessingService _paymentService;
    private readonly PosDbContext _context;
    private readonly ITestOutputHelper _output;

    // Shared test data IDs
    private readonly Guid _businessId;
    private readonly Guid _shopId;
    private readonly Guid _userId;
    private readonly Guid _deviceId;
    private readonly Guid _productId;

    public PaymentProcessingServiceTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddSharedCoreInMemory();
        _serviceProvider = services.BuildServiceProvider();

        _paymentService = _serviceProvider.GetRequiredService<IPaymentProcessingService>();
        _context = _serviceProvider.GetRequiredService<PosDbContext>();

        _businessId = Guid.NewGuid();
        _shopId = Guid.NewGuid();
        _userId = Guid.NewGuid();
        _deviceId = Guid.NewGuid();
        _productId = Guid.NewGuid();

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
            Address = "123 Test Street",
            Phone = "555-0100",
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

        var product = new Product
        {
            Id = _productId,
            ShopId = _shopId,
            Name = "Test Product",
            Barcode = "1234567890",
            UnitPrice = 10.00m,
            IsActive = true
        };
        _context.Products.Add(product);

        // Seed a license for the device used by ICurrentUserService mock
        var currentUserService = _serviceProvider.GetRequiredService<ICurrentUserService>();
        var licenseDeviceId = currentUserService.GetDeviceId();
        var license = new License
        {
            Id = Guid.NewGuid(),
            LicenseKey = "TEST-LICENSE-KEY-PAY-001",
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

    /// <summary>
    /// Creates a minimal Sale with one active SaleItem for use in payment tests.
    /// The sale and item are NOT persisted to the DB — they are in-memory objects
    /// with navigation properties set directly, which is sufficient for
    /// CalculateFinalTotalsAsync (it reads sale.Items directly).
    /// </summary>
    private Sale CreateSaleWithItem(decimal unitPrice = 10.00m, int quantity = 2)
    {
        var product = new Product
        {
            Id = _productId,
            ShopId = _shopId,
            Name = "Test Product",
            Barcode = "1234567890",
            UnitPrice = unitPrice
        };

        var item = new SaleItem
        {
            Id = Guid.NewGuid(),
            ProductId = _productId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TotalPrice = unitPrice * quantity,
            IsWeightBased = false,
            IsDeleted = false,
            Product = product
        };

        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            UserId = _userId,
            DeviceId = _deviceId,
            InvoiceNumber = $"INV-TEST-{Guid.NewGuid():N}".Substring(0, 20),
            TotalAmount = unitPrice * quantity,
            Status = SaleStatus.Active,
            Items = new List<SaleItem> { item },
            AppliedDiscounts = new List<SaleDiscount>()
        };

        item.SaleId = sale.Id;
        return sale;
    }

    /// <summary>
    /// Creates a completed Sale persisted to the DB for refund tests.
    /// </summary>
    private async Task<Sale> CreateCompletedSaleInDbAsync(decimal totalAmount = 20.00m)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            UserId = _userId,
            DeviceId = _deviceId,
            InvoiceNumber = $"INV-{Guid.NewGuid():N}".Substring(0, 20),
            TotalAmount = totalAmount,
            Status = SaleStatus.Completed,
            CompletedAt = DateTime.UtcNow,
            Items = new List<SaleItem>(),
            AppliedDiscounts = new List<SaleDiscount>()
        };
        _context.Sales.Add(sale);
        await _context.SaveChangesAsync();
        return sale;
    }

    // =========================================================================
    // ValidatePaymentMethodAsync Tests
    // =========================================================================

    [Fact]
    public async Task ValidatePaymentMethod_CashWithPositiveAmount_ReturnsValid()
    {
        var result = await _paymentService.ValidatePaymentMethodAsync(PaymentMethod.Cash, 50.00m);

        Assert.True(result.IsValid);
        Assert.Equal(PaymentMethod.Cash, result.PaymentMethod);
        Assert.Equal(50.00m, result.Amount);
        _output.WriteLine($"Cash validation: IsValid={result.IsValid}");
    }

    [Fact]
    public async Task ValidatePaymentMethod_Card_ReturnsValidAndRequiresExactAmount()
    {
        var result = await _paymentService.ValidatePaymentMethodAsync(PaymentMethod.Card, 100.00m);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresExactAmount);
        _output.WriteLine($"Card validation: IsValid={result.IsValid}, RequiresExactAmount={result.RequiresExactAmount}");
    }

    [Fact]
    public async Task ValidatePaymentMethod_DigitalPayment_ReturnsValidAndRequiresExactAmount()
    {
        var result = await _paymentService.ValidatePaymentMethodAsync(PaymentMethod.DigitalPayment, 75.00m);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresExactAmount);
    }

    [Fact]
    public async Task ValidatePaymentMethod_BankTransfer_ReturnsValidAndRequiresExactAmount()
    {
        var result = await _paymentService.ValidatePaymentMethodAsync(PaymentMethod.BankTransfer, 200.00m);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresExactAmount);
    }

    [Fact]
    public async Task ValidatePaymentMethod_ZeroAmount_ReturnsInvalid()
    {
        var result = await _paymentService.ValidatePaymentMethodAsync(PaymentMethod.Cash, 0m);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.ValidationMessage!);
        _output.WriteLine($"Zero amount rejected: {result.ValidationMessage}");
    }

    [Fact]
    public async Task ValidatePaymentMethod_NegativeAmount_ReturnsInvalid()
    {
        var result = await _paymentService.ValidatePaymentMethodAsync(PaymentMethod.Cash, -10.00m);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.ValidationErrors);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.DigitalPayment)]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.Credit)]
    public async Task ValidatePaymentMethod_AllDefinedEnumValues_ReturnValid(PaymentMethod method)
    {
        var result = await _paymentService.ValidatePaymentMethodAsync(method, 50.00m);

        Assert.True(result.IsValid, $"Expected {method} to be valid but got: {result.ValidationMessage}");
        _output.WriteLine($"{method}: IsValid={result.IsValid}");
    }

    // =========================================================================
    // ProcessPaymentAsync Tests
    // =========================================================================

    [Fact]
    public async Task ProcessPayment_CashExactAmount_SucceedsWithZeroChange()
    {
        var sale = CreateSaleWithItem(unitPrice: 10.00m, quantity: 2); // total = 20.00
        // With no tax/discount configured, FinalTotal = 20.00
        var result = await _paymentService.ProcessPaymentAsync(sale, PaymentMethod.Cash, 20.00m);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.ChangeAmount);
        _output.WriteLine($"Exact cash: FinalTotal={result.FinalTotal}, Change={result.ChangeAmount}");
    }

    [Fact]
    public async Task ProcessPayment_CashOverpayment_SucceedsWithCorrectChange()
    {
        var sale = CreateSaleWithItem(unitPrice: 10.00m, quantity: 2); // subtotal = 20.00
        var result = await _paymentService.ProcessPaymentAsync(sale, PaymentMethod.Cash, 30.00m);

        Assert.True(result.IsSuccess);
        Assert.True(result.ChangeAmount > 0, "Change should be positive for overpayment");
        Assert.Equal(result.AmountPaid - result.FinalTotal, result.ChangeAmount);
        _output.WriteLine($"Overpayment: Paid={result.AmountPaid}, FinalTotal={result.FinalTotal}, Change={result.ChangeAmount}");
    }

    [Fact]
    public async Task ProcessPayment_CashInsufficientAmount_FailsWithStatePreserved()
    {
        var sale = CreateSaleWithItem(unitPrice: 10.00m, quantity: 2); // subtotal = 20.00
        var result = await _paymentService.ProcessPaymentAsync(sale, PaymentMethod.Cash, 5.00m);

        Assert.False(result.IsSuccess);
        Assert.True(result.SaleStatePreserved);
        Assert.True(result.FinalTotal > 0, "FinalTotal should be populated even on failure");
        Assert.NotEmpty(result.ErrorMessage!);
        _output.WriteLine($"Insufficient cash: FinalTotal={result.FinalTotal}, ErrorMessage={result.ErrorMessage}");
    }

    [Fact]
    public async Task ProcessPayment_CardPayment_Succeeds()
    {
        var sale = CreateSaleWithItem(unitPrice: 15.00m, quantity: 1);
        // Card payment: any amount >= total is accepted (RequiresExactAmount means no change given)
        var result = await _paymentService.ProcessPaymentAsync(sale, PaymentMethod.Card, 15.00m);

        Assert.True(result.IsSuccess);
        _output.WriteLine($"Card payment: IsSuccess={result.IsSuccess}, FinalTotal={result.FinalTotal}");
    }

    [Fact]
    public async Task ProcessPayment_NullSale_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _paymentService.ProcessPaymentAsync(null!, PaymentMethod.Cash, 50.00m));
    }

    [Fact]
    public async Task ProcessPayment_SuccessfulPayment_InvoiceNumberMatchesSale()
    {
        var sale = CreateSaleWithItem();
        var result = await _paymentService.ProcessPaymentAsync(sale, PaymentMethod.Cash, 100.00m);

        Assert.True(result.IsSuccess);
        Assert.Equal(sale.InvoiceNumber, result.InvoiceNumber);
        _output.WriteLine($"InvoiceNumber: sale={sale.InvoiceNumber}, result={result.InvoiceNumber}");
    }

    [Fact]
    public async Task ProcessPayment_SuccessfulPayment_FinalTotalIsPositive()
    {
        var sale = CreateSaleWithItem(unitPrice: 5.00m, quantity: 3);
        var result = await _paymentService.ProcessPaymentAsync(sale, PaymentMethod.Cash, 100.00m);

        Assert.True(result.IsSuccess);
        Assert.True(result.FinalTotal > 0);
        _output.WriteLine($"FinalTotal={result.FinalTotal}");
    }

    [Fact]
    public async Task ProcessPayment_FailedPayment_SaleStatePreservedIsTrue()
    {
        var sale = CreateSaleWithItem(unitPrice: 50.00m, quantity: 1);
        // Pay zero — validation will fail
        var result = await _paymentService.ProcessPaymentAsync(sale, PaymentMethod.Cash, 0m);

        Assert.False(result.IsSuccess);
        Assert.True(result.SaleStatePreserved);
    }

    // =========================================================================
    // GenerateReceiptAsync Tests
    // =========================================================================

    private PaymentResult BuildPaymentResult(Sale sale, decimal finalTotal = 20.00m, decimal amountPaid = 20.00m)
    {
        return new PaymentResult
        {
            IsSuccess = true,
            TransactionId = Guid.NewGuid(),
            SaleId = sale.Id,
            PaymentMethod = PaymentMethod.Cash,
            FinalTotal = finalTotal,
            AmountPaid = amountPaid,
            ChangeAmount = amountPaid - finalTotal,
            Subtotal = finalTotal,
            TotalDiscount = 0,
            MembershipDiscountAmount = 0,
            TotalTax = 0,
            ProcessedAt = DateTime.UtcNow,
            InvoiceNumber = sale.InvoiceNumber
        };
    }

    [Fact]
    public async Task GenerateReceipt_HasCorrectInvoiceNumber()
    {
        var sale = CreateSaleWithItem();
        var paymentResult = BuildPaymentResult(sale);

        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        Assert.Equal(sale.InvoiceNumber, receipt.InvoiceNumber);
    }

    [Fact]
    public async Task GenerateReceipt_HasCorrectSaleId()
    {
        var sale = CreateSaleWithItem();
        var paymentResult = BuildPaymentResult(sale);

        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        Assert.Equal(sale.Id, receipt.SaleId);
    }

    [Fact]
    public async Task GenerateReceipt_LineItemsCountMatchesNonDeletedItems()
    {
        var sale = CreateSaleWithItem();
        // Add a deleted item — it should be excluded from the receipt
        var deletedItem = new SaleItem
        {
            Id = Guid.NewGuid(),
            SaleId = sale.Id,
            ProductId = _productId,
            Quantity = 1,
            UnitPrice = 5.00m,
            TotalPrice = 5.00m,
            IsDeleted = true
        };
        sale.Items.Add(deletedItem);

        var paymentResult = BuildPaymentResult(sale);
        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        var expectedCount = sale.Items.Count(i => !i.IsDeleted);
        Assert.Equal(expectedCount, receipt.LineItems.Count);
        _output.WriteLine($"Active items: {expectedCount}, Receipt line items: {receipt.LineItems.Count}");
    }

    [Fact]
    public async Task GenerateReceipt_ValidSaleWithItems_IsCompleteTrue()
    {
        var sale = CreateSaleWithItem();
        var paymentResult = BuildPaymentResult(sale);

        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        Assert.True(receipt.IsComplete);
    }

    [Fact]
    public async Task GenerateReceipt_NullSale_ThrowsArgumentNullException()
    {
        var dummyResult = new PaymentResult { IsSuccess = true };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _paymentService.GenerateReceiptAsync(null!, dummyResult));
    }

    [Fact]
    public async Task GenerateReceipt_NullPaymentResult_ThrowsArgumentNullException()
    {
        var sale = CreateSaleWithItem();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _paymentService.GenerateReceiptAsync(sale, null!));
    }

    [Fact]
    public async Task GenerateReceipt_FinalTotalMatchesPaymentResult()
    {
        var sale = CreateSaleWithItem();
        var paymentResult = BuildPaymentResult(sale, finalTotal: 18.50m, amountPaid: 20.00m);

        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        Assert.Equal(paymentResult.FinalTotal, receipt.FinalTotal);
    }

    [Fact]
    public async Task GenerateReceipt_AmountPaidMatchesPaymentResult()
    {
        var sale = CreateSaleWithItem();
        var paymentResult = BuildPaymentResult(sale, finalTotal: 18.50m, amountPaid: 25.00m);

        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        Assert.Equal(paymentResult.AmountPaid, receipt.AmountPaid);
    }

    [Fact]
    public async Task GenerateReceipt_ChangeAmountMatchesPaymentResult()
    {
        var sale = CreateSaleWithItem();
        var paymentResult = BuildPaymentResult(sale, finalTotal: 18.50m, amountPaid: 25.00m);

        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        Assert.Equal(paymentResult.ChangeAmount, receipt.ChangeAmount);
    }

    [Fact]
    public async Task GenerateReceipt_WithCustomer_CustomerNamePopulated()
    {
        var sale = CreateSaleWithItem();
        sale.Customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = "Jane Doe",
            MembershipNumber = "MEM-001",
            IsActive = true
        };
        sale.CustomerId = sale.Customer.Id;

        var paymentResult = BuildPaymentResult(sale);
        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        Assert.Equal("Jane Doe", receipt.CustomerName);
        _output.WriteLine($"CustomerName on receipt: {receipt.CustomerName}");
    }

    [Fact]
    public async Task GenerateReceipt_WithWeightBasedItem_LineItemIsWeightBasedTrue()
    {
        var sale = CreateSaleWithItem();
        // Replace the regular item with a weight-based one
        var weightItem = new SaleItem
        {
            Id = Guid.NewGuid(),
            SaleId = sale.Id,
            ProductId = _productId,
            Quantity = 1,
            UnitPrice = 0m,
            Weight = 1.5m,
            RatePerKilogram = 8.00m,
            TotalPrice = 12.00m,
            IsWeightBased = true,
            IsDeleted = false,
            Product = new Product { Id = _productId, Name = "Bulk Rice", ShopId = _shopId }
        };
        sale.Items = new List<SaleItem> { weightItem };

        var paymentResult = BuildPaymentResult(sale, finalTotal: 12.00m, amountPaid: 12.00m);
        var receipt = await _paymentService.GenerateReceiptAsync(sale, paymentResult);

        Assert.Single(receipt.LineItems);
        Assert.True(receipt.LineItems[0].IsWeightBased);
        _output.WriteLine($"Weight-based line item: IsWeightBased={receipt.LineItems[0].IsWeightBased}");
    }

    // =========================================================================
    // ProcessRefundAsync Tests
    // =========================================================================

    [Fact]
    public async Task ProcessRefund_CompletedSale_Succeeds()
    {
        var sale = await CreateCompletedSaleInDbAsync(totalAmount: 50.00m);

        var result = await _paymentService.ProcessRefundAsync(sale.Id, 50.00m, "Customer returned item");

        Assert.True(result.IsSuccess);
        Assert.Equal(sale.Id, result.OriginalSaleId);
        _output.WriteLine($"Refund succeeded: RefundTransactionId={result.RefundTransactionId}");
    }

    [Fact]
    public async Task ProcessRefund_NonCompletedSale_Fails()
    {
        // Create a Draft sale in the DB
        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            UserId = _userId,
            DeviceId = _deviceId,
            InvoiceNumber = $"INV-DRAFT-{Guid.NewGuid():N}".Substring(0, 20),
            TotalAmount = 30.00m,
            Status = SaleStatus.Draft
        };
        _context.Sales.Add(sale);
        await _context.SaveChangesAsync();

        var result = await _paymentService.ProcessRefundAsync(sale.Id, 30.00m, "Test refund");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.ErrorMessage!);
        _output.WriteLine($"Draft sale refund rejected: {result.ErrorMessage}");
    }

    [Fact]
    public async Task ProcessRefund_AmountExceedsSaleTotal_Fails()
    {
        var sale = await CreateCompletedSaleInDbAsync(totalAmount: 20.00m);

        var result = await _paymentService.ProcessRefundAsync(sale.Id, 999.00m, "Excessive refund");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.ErrorMessage!);
        _output.WriteLine($"Excessive refund rejected: {result.ErrorMessage}");
    }

    [Fact]
    public async Task ProcessRefund_PartialAmount_IsPartialRefundTrue()
    {
        var sale = await CreateCompletedSaleInDbAsync(totalAmount: 100.00m);

        var result = await _paymentService.ProcessRefundAsync(sale.Id, 40.00m, "Partial return");

        Assert.True(result.IsSuccess);
        Assert.True(result.IsPartialRefund);
        _output.WriteLine($"Partial refund: IsPartialRefund={result.IsPartialRefund}");
    }

    [Fact]
    public async Task ProcessRefund_FullAmount_IsPartialRefundFalse()
    {
        var sale = await CreateCompletedSaleInDbAsync(totalAmount: 60.00m);

        var result = await _paymentService.ProcessRefundAsync(sale.Id, 60.00m, "Full return");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsPartialRefund);
        _output.WriteLine($"Full refund: IsPartialRefund={result.IsPartialRefund}");
    }

    [Fact]
    public async Task ProcessRefund_EmptySaleId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _paymentService.ProcessRefundAsync(Guid.Empty, 10.00m, "Test"));
    }

    [Fact]
    public async Task ProcessRefund_ZeroAmount_ThrowsArgumentOutOfRangeException()
    {
        var saleId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _paymentService.ProcessRefundAsync(saleId, 0m, "Test"));
    }

    [Fact]
    public async Task ProcessRefund_EmptyReason_ThrowsArgumentException()
    {
        var saleId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _paymentService.ProcessRefundAsync(saleId, 10.00m, string.Empty));
    }

    // =========================================================================
    // GetAvailablePaymentMethodsAsync Tests
    // =========================================================================

    [Fact]
    public async Task GetAvailablePaymentMethods_ValidShopId_ReturnsNonEmptyCollection()
    {
        var methods = await _paymentService.GetAvailablePaymentMethodsAsync(_shopId);

        Assert.NotNull(methods);
        Assert.NotEmpty(methods);
        _output.WriteLine($"Available methods for shop: {string.Join(", ", methods)}");
    }

    [Fact]
    public async Task GetAvailablePaymentMethods_EmptyShopId_ReturnsDefaultMethods()
    {
        // Empty Guid should not throw — returns default methods
        var methods = await _paymentService.GetAvailablePaymentMethodsAsync(Guid.Empty);

        Assert.NotNull(methods);
        Assert.NotEmpty(methods);
        _output.WriteLine($"Default methods returned for empty shopId: {string.Join(", ", methods)}");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
