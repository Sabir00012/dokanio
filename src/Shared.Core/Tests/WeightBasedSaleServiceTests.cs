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
/// Unit tests for weight-based product handling in SaleService.
/// Covers: AddWeightBasedItemToSaleAsync, UpdateItemWeightAsync, and weight validation.
/// Requirements: 2.3, 5.1, 5.2, 5.3, 5.4, 5.5
/// </summary>
public class WeightBasedSaleServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ISaleService _saleService;
    private readonly PosDbContext _context;
    private readonly ITestOutputHelper _output;

    // Shared test data
    private readonly Guid _deviceId;
    private readonly Guid _userId;
    private readonly Guid _shopId;
    private readonly Guid _businessId;
    private Guid _weightProductId;
    private Guid _regularProductId;

    public WeightBasedSaleServiceTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddSharedCoreInMemory();
        _serviceProvider = services.BuildServiceProvider();

        _saleService = _serviceProvider.GetRequiredService<ISaleService>();
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

        // Seed license
        var currentUserService = _serviceProvider.GetRequiredService<ICurrentUserService>();
        var licenseDeviceId = currentUserService.GetDeviceId();
        var license = new License
        {
            Id = Guid.NewGuid(),
            LicenseKey = "TEST-LICENSE-KEY-WEIGHT",
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

        // Weight-based product
        _weightProductId = Guid.NewGuid();
        var weightProduct = new Product
        {
            Id = _weightProductId,
            ShopId = _shopId,
            Name = "Bulk Rice",
            IsWeightBased = true,
            RatePerKilogram = 2.50m,
            WeightPrecision = 3,
            MinWeightKg = 0.1m,
            MaxWeightKg = 50.0m,
            IsActive = true,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.NotSynced
        };
        _context.Products.Add(weightProduct);

        // Regular product
        _regularProductId = Guid.NewGuid();
        var regularProduct = new Product
        {
            Id = _regularProductId,
            ShopId = _shopId,
            Name = "Bottled Water",
            IsWeightBased = false,
            UnitPrice = 1.50m,
            IsActive = true,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.NotSynced
        };
        _context.Products.Add(regularProduct);

        // Add stock for both products
        _context.Stock.Add(new Stock
        {
            Id = Guid.NewGuid(),
            ProductId = _weightProductId,
            ShopId = _shopId,
            Quantity = 1000,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.NotSynced
        });
        _context.Stock.Add(new Stock
        {
            Id = Guid.NewGuid(),
            ProductId = _regularProductId,
            ShopId = _shopId,
            Quantity = 100,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.NotSynced
        });

        await _context.SaveChangesAsync();
    }

    private async Task<Sale> CreateActiveSaleAsync()
    {
        return await _saleService.CreateSaleAsync(_saleService.GenerateInvoiceNumber(), _deviceId);
    }

    // =========================================================================
    // AddWeightBasedItemToSaleAsync Tests (Requirements 2.3, 5.1, 5.2, 5.3)
    // =========================================================================

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_ValidWeight_ShouldAddItemWithCorrectPrice()
    {
        var sale = await CreateActiveSaleAsync();

        // 2.500 kg × $2.50/kg = $6.25
        var updatedSale = await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 2.500m);

        Assert.NotNull(updatedSale);
        Assert.Equal(6.25m, updatedSale.TotalAmount);
        _output.WriteLine($"Added 2.500 kg at $2.50/kg = ${updatedSale.TotalAmount}");
    }

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_ShouldSetIsWeightBasedOnItem()
    {
        var sale = await CreateActiveSaleAsync();

        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.500m);

        var items = _context.SaleItems.Where(si => si.SaleId == sale.Id && !si.IsDeleted).ToList();
        Assert.Single(items);
        Assert.True(items[0].IsWeightBased, "SaleItem.IsWeightBased should be true for weight-based products");
        _output.WriteLine($"SaleItem.IsWeightBased = {items[0].IsWeightBased}");
    }

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_ShouldSetWeightAndRateOnItem()
    {
        var sale = await CreateActiveSaleAsync();

        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 2.500m);

        var items = _context.SaleItems.Where(si => si.SaleId == sale.Id && !si.IsDeleted).ToList();
        Assert.Single(items);
        Assert.Equal(2.500m, items[0].Weight);
        Assert.Equal(2.50m, items[0].RatePerKilogram);
        _output.WriteLine($"Weight={items[0].Weight}, Rate={items[0].RatePerKilogram}");
    }

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_ShouldRoundWeightToPrecision()
    {
        var sale = await CreateActiveSaleAsync();

        // Product has precision 3; input 2.5678 should round to 2.568
        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 2.5678m);

        var items = _context.SaleItems.Where(si => si.SaleId == sale.Id && !si.IsDeleted).ToList();
        Assert.Single(items);
        Assert.Equal(2.568m, items[0].Weight);
        _output.WriteLine($"Input 2.5678 rounded to {items[0].Weight} (precision 3)");
    }

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_WeightBelowProductMinimum_ShouldThrowArgumentException()
    {
        var sale = await CreateActiveSaleAsync();

        // Product minimum is 0.1 kg; 0.05 is below it
        await Assert.ThrowsAsync<ArgumentException>(
            () => _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 0.05m));

        _output.WriteLine("Correctly rejected weight 0.05 kg (below product minimum 0.1 kg)");
    }

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_WeightAboveProductMaximum_ShouldThrowArgumentException()
    {
        var sale = await CreateActiveSaleAsync();

        // Product maximum is 50.0 kg; 60.0 is above it
        await Assert.ThrowsAsync<ArgumentException>(
            () => _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 60.0m));

        _output.WriteLine("Correctly rejected weight 60.0 kg (above product maximum 50.0 kg)");
    }

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_ZeroWeight_ShouldThrowArgumentOutOfRangeException()
    {
        var sale = await CreateActiveSaleAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 0m));
    }

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_RegularProduct_ShouldThrowInvalidOperationException()
    {
        var sale = await CreateActiveSaleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _regularProductId, 1.0m));

        _output.WriteLine("Correctly rejected adding regular product via weight-based method");
    }

    [Fact]
    public async Task AddWeightBasedItemToSaleAsync_ShouldTransitionSaleToActive()
    {
        var sale = await CreateActiveSaleAsync();
        Assert.Equal(SaleStatus.Draft, sale.Status);

        var updatedSale = await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.0m);

        Assert.Equal(SaleStatus.Active, updatedSale.Status);
        _output.WriteLine("Sale transitioned from Draft to Active after adding weight-based item");
    }

    // =========================================================================
    // UpdateItemWeightAsync Tests (Requirements 5.4, 5.5)
    // =========================================================================

    [Fact]
    public async Task UpdateItemWeightAsync_ValidNewWeight_ShouldUpdateWeightAndRecalculateTotal()
    {
        var sale = await CreateActiveSaleAsync();
        var saleWithItem = await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.000m);

        // Initial: 1.000 kg × $2.50/kg = $2.50
        Assert.Equal(2.50m, saleWithItem.TotalAmount);

        // Update to 3.000 kg × $2.50/kg = $7.50
        var updatedSale = await _saleService.UpdateItemWeightAsync(
            sale.Id,
            _context.SaleItems.First(si => si.SaleId == sale.Id && !si.IsDeleted).Id,
            3.000m);

        Assert.Equal(7.50m, updatedSale.TotalAmount);
        _output.WriteLine($"Weight updated: 1.000 kg → 3.000 kg, total: $2.50 → ${updatedSale.TotalAmount}");
    }

    [Fact]
    public async Task UpdateItemWeightAsync_ShouldUpdateItemWeightInDatabase()
    {
        var sale = await CreateActiveSaleAsync();
        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.000m);
        var itemId = _context.SaleItems.First(si => si.SaleId == sale.Id && !si.IsDeleted).Id;

        await _saleService.UpdateItemWeightAsync(sale.Id, itemId, 2.500m);

        var updatedItem = _context.SaleItems.First(si => si.Id == itemId);
        Assert.Equal(2.500m, updatedItem.Weight);
        _output.WriteLine($"Item weight updated to {updatedItem.Weight} kg");
    }

    [Fact]
    public async Task UpdateItemWeightAsync_ShouldRoundWeightToPrecision()
    {
        var sale = await CreateActiveSaleAsync();
        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.000m);
        var itemId = _context.SaleItems.First(si => si.SaleId == sale.Id && !si.IsDeleted).Id;

        // Input 2.5678 should round to 2.568 (precision 3)
        await _saleService.UpdateItemWeightAsync(sale.Id, itemId, 2.5678m);

        var updatedItem = _context.SaleItems.First(si => si.Id == itemId);
        Assert.Equal(2.568m, updatedItem.Weight);
        _output.WriteLine($"Weight 2.5678 rounded to {updatedItem.Weight} (precision 3)");
    }

    [Fact]
    public async Task UpdateItemWeightAsync_WeightBelowProductMinimum_ShouldThrowArgumentException()
    {
        var sale = await CreateActiveSaleAsync();
        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.000m);
        var itemId = _context.SaleItems.First(si => si.SaleId == sale.Id && !si.IsDeleted).Id;

        // Product minimum is 0.1 kg
        await Assert.ThrowsAsync<ArgumentException>(
            () => _saleService.UpdateItemWeightAsync(sale.Id, itemId, 0.05m));

        _output.WriteLine("Correctly rejected weight update to 0.05 kg (below product minimum 0.1 kg)");
    }

    [Fact]
    public async Task UpdateItemWeightAsync_WeightAboveProductMaximum_ShouldThrowArgumentException()
    {
        var sale = await CreateActiveSaleAsync();
        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.000m);
        var itemId = _context.SaleItems.First(si => si.SaleId == sale.Id && !si.IsDeleted).Id;

        // Product maximum is 50.0 kg
        await Assert.ThrowsAsync<ArgumentException>(
            () => _saleService.UpdateItemWeightAsync(sale.Id, itemId, 55.0m));

        _output.WriteLine("Correctly rejected weight update to 55.0 kg (above product maximum 50.0 kg)");
    }

    [Fact]
    public async Task UpdateItemWeightAsync_ZeroWeight_ShouldThrowArgumentOutOfRangeException()
    {
        var sale = await CreateActiveSaleAsync();
        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.000m);
        var itemId = _context.SaleItems.First(si => si.SaleId == sale.Id && !si.IsDeleted).Id;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _saleService.UpdateItemWeightAsync(sale.Id, itemId, 0m));
    }

    [Fact]
    public async Task UpdateItemWeightAsync_NonWeightBasedItem_ShouldThrowInvalidOperationException()
    {
        var sale = await CreateActiveSaleAsync();
        await _saleService.AddItemToSaleAsync(sale.Id, _regularProductId, 2, 1.50m);
        var itemId = _context.SaleItems.First(si => si.SaleId == sale.Id && !si.IsDeleted).Id;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _saleService.UpdateItemWeightAsync(sale.Id, itemId, 1.0m));

        _output.WriteLine("Correctly rejected weight update on non-weight-based item");
    }

    [Fact]
    public async Task UpdateItemWeightAsync_CompletedSale_ShouldThrowInvalidOperationException()
    {
        var sale = await CreateActiveSaleAsync();
        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.000m);
        var itemId = _context.SaleItems.First(si => si.SaleId == sale.Id && !si.IsDeleted).Id;

        await _saleService.CompleteSaleAsync(sale.Id, PaymentMethod.Cash);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _saleService.UpdateItemWeightAsync(sale.Id, itemId, 2.0m));

        _output.WriteLine("Correctly rejected weight update on completed sale");
    }

    [Fact]
    public async Task UpdateItemWeightAsync_WithMultipleItems_ShouldRecalculateTotalCorrectly()
    {
        var sale = await CreateActiveSaleAsync();

        // Add weight-based item: 1.000 kg × $2.50 = $2.50
        await _saleService.AddWeightBasedItemToSaleAsync(sale.Id, _weightProductId, 1.000m);

        // Add regular item: 2 × $1.50 = $3.00
        await _saleService.AddItemToSaleAsync(sale.Id, _regularProductId, 2, 1.50m);

        // Total should be $2.50 + $3.00 = $5.50
        var saleAfterAdd = await _saleService.GetSaleByIdAsync(sale.Id);
        Assert.Equal(5.50m, saleAfterAdd!.TotalAmount);

        // Update weight to 2.000 kg × $2.50 = $5.00
        var weightItemId = _context.SaleItems
            .First(si => si.SaleId == sale.Id && !si.IsDeleted && si.IsWeightBased).Id;

        var updatedSale = await _saleService.UpdateItemWeightAsync(sale.Id, weightItemId, 2.000m);

        // New total: $5.00 + $3.00 = $8.00
        Assert.Equal(8.00m, updatedSale.TotalAmount);
        _output.WriteLine($"Multi-item total after weight update: ${updatedSale.TotalAmount}");
    }

    [Fact]
    public async Task UpdateItemWeightAsync_NonExistentItem_ShouldThrowArgumentException()
    {
        var sale = await CreateActiveSaleAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _saleService.UpdateItemWeightAsync(sale.Id, Guid.NewGuid(), 1.0m));
    }

    [Fact]
    public async Task UpdateItemWeightAsync_NonExistentSale_ShouldThrowArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _saleService.UpdateItemWeightAsync(Guid.NewGuid(), Guid.NewGuid(), 1.0m));
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
