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
/// Unit tests for InventoryUpdater.
/// Covers:
/// - Stock level reduction for all items in a completed sale (Req 6.3)
/// - Customer purchase history update and membership points (Req 6.6)
/// - Rollback behavior for failed transactions
/// - Concurrent inventory management scenarios
/// </summary>
public class InventoryUpdaterTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IInventoryUpdater _inventoryUpdater;
    private readonly PosDbContext _context;
    private readonly ITestOutputHelper _output;

    private readonly Guid _businessId;
    private readonly Guid _shopId;
    private readonly Guid _userId;
    private readonly Guid _deviceId;

    public InventoryUpdaterTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddSharedCoreInMemory();
        _serviceProvider = services.BuildServiceProvider();

        _inventoryUpdater = _serviceProvider.GetRequiredService<IInventoryUpdater>();
        _context = _serviceProvider.GetRequiredService<PosDbContext>();

        _businessId = Guid.NewGuid();
        _shopId = Guid.NewGuid();
        _userId = Guid.NewGuid();
        _deviceId = Guid.NewGuid();

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

        // Seed license for ICurrentUserService mock
        var currentUserService = _serviceProvider.GetRequiredService<ICurrentUserService>();
        var licenseDeviceId = currentUserService.GetDeviceId();
        var license = new License
        {
            Id = Guid.NewGuid(),
            LicenseKey = "TEST-LICENSE-KEY-INV-001",
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
    // Helpers
    // =========================================================================

    private async Task<(Product product, Stock stock)> CreateProductWithStockAsync(
        string name, int stockQty, bool isWeightBased = false)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            Name = name,
            UnitPrice = 10.00m,
            IsActive = true,
            IsWeightBased = isWeightBased,
            RatePerKilogram = isWeightBased ? 8.00m : null,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.NotSynced
        };
        _context.Products.Add(product);

        var stock = new Stock
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            ProductId = product.Id,
            Quantity = stockQty,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.NotSynced
        };
        _context.Stock.Add(stock);

        await _context.SaveChangesAsync();
        return (product, stock);
    }

    private Sale BuildSaleWithItems(List<(Product product, int quantity, bool isWeightBased)> items,
        Customer? customer = null)
    {
        var saleItems = items.Select(i =>
        {
            var item = new SaleItem
            {
                Id = Guid.NewGuid(),
                ProductId = i.product.Id,
                Quantity = i.quantity,
                UnitPrice = i.product.UnitPrice,
                TotalPrice = i.isWeightBased ? 1.5m * 8.00m : i.product.UnitPrice * i.quantity,
                IsWeightBased = i.isWeightBased,
                Weight = i.isWeightBased ? 1.5m : null,
                RatePerKilogram = i.isWeightBased ? 8.00m : null,
                IsDeleted = false,
                Product = i.product
            };
            return item;
        }).ToList();

        var totalAmount = saleItems.Sum(si => si.TotalPrice);

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
            CustomerId = customer?.Id,
            Customer = customer,
            Items = saleItems,
            AppliedDiscounts = new List<SaleDiscount>()
        };

        foreach (var item in saleItems)
            item.SaleId = sale.Id;

        return sale;
    }

    private async Task<Customer> CreateCustomerAsync(
        string name, decimal totalSpent = 0, MembershipTier tier = MembershipTier.Bronze)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            MembershipNumber = $"MEM-{Guid.NewGuid():N}".Substring(0, 12),
            Name = name,
            Email = $"{name.ToLower().Replace(" ", "")}@test.com",
            Tier = tier,
            TotalSpent = totalSpent,
            VisitCount = 0,
            IsActive = true,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.NotSynced
        };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return customer;
    }

    // =========================================================================
    // ReduceStockLevelsAsync Tests (Req 6.3)
    // =========================================================================

    [Fact]
    public async Task ReduceStockLevels_SingleItem_ReducesStockByQuantity()
    {
        var (product, stock) = await CreateProductWithStockAsync("Widget", 20);
        var sale = BuildSaleWithItems(new() { (product, 5, false) });

        var result = await _inventoryUpdater.ReduceStockLevelsAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ItemsUpdated);

        // Verify stock was actually reduced in the DB
        var updatedStock = await _context.Stock.FindAsync(stock.Id);
        Assert.Equal(15, updatedStock!.Quantity); // 20 - 5 = 15

        _output.WriteLine($"Stock reduced: 20 → {updatedStock.Quantity}");
    }

    [Fact]
    public async Task ReduceStockLevels_MultipleItems_ReducesAllItemsStock()
    {
        var (product1, stock1) = await CreateProductWithStockAsync("Product A", 10);
        var (product2, stock2) = await CreateProductWithStockAsync("Product B", 15);
        var sale = BuildSaleWithItems(new()
        {
            (product1, 3, false),
            (product2, 7, false)
        });

        var result = await _inventoryUpdater.ReduceStockLevelsAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.ItemsUpdated);

        var updatedStock1 = await _context.Stock.FindAsync(stock1.Id);
        var updatedStock2 = await _context.Stock.FindAsync(stock2.Id);

        Assert.Equal(7, updatedStock1!.Quantity);  // 10 - 3 = 7
        Assert.Equal(8, updatedStock2!.Quantity);  // 15 - 7 = 8

        _output.WriteLine($"Product A: 10 → {updatedStock1.Quantity}, Product B: 15 → {updatedStock2.Quantity}");
    }

    [Fact]
    public async Task ReduceStockLevels_WeightBasedItem_DeductsOneUnit()
    {
        var (product, stock) = await CreateProductWithStockAsync("Bulk Rice", 50, isWeightBased: true);
        var sale = BuildSaleWithItems(new() { (product, 1, true) });

        var result = await _inventoryUpdater.ReduceStockLevelsAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ItemsUpdated);

        var updatedStock = await _context.Stock.FindAsync(stock.Id);
        Assert.Equal(49, updatedStock!.Quantity); // 50 - 1 = 49 (weight-based deducts 1 unit)

        _output.WriteLine($"Weight-based stock: 50 → {updatedStock.Quantity}");
    }

    [Fact]
    public async Task ReduceStockLevels_DeletedItemsSkipped_OnlyActiveItemsReduced()
    {
        var (product1, stock1) = await CreateProductWithStockAsync("Active Product", 10);
        var (product2, stock2) = await CreateProductWithStockAsync("Deleted Product", 10);

        var sale = BuildSaleWithItems(new() { (product1, 2, false) });

        // Add a deleted item manually
        var deletedItem = new SaleItem
        {
            Id = Guid.NewGuid(),
            SaleId = sale.Id,
            ProductId = product2.Id,
            Quantity = 5,
            UnitPrice = 10m,
            TotalPrice = 50m,
            IsDeleted = true,
            Product = product2
        };
        sale.Items.Add(deletedItem);

        var result = await _inventoryUpdater.ReduceStockLevelsAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ItemsUpdated); // Only the active item

        var updatedStock1 = await _context.Stock.FindAsync(stock1.Id);
        var updatedStock2 = await _context.Stock.FindAsync(stock2.Id);

        Assert.Equal(8, updatedStock1!.Quantity);  // 10 - 2 = 8
        Assert.Equal(10, updatedStock2!.Quantity); // Unchanged (deleted item skipped)

        _output.WriteLine($"Active: 10→{updatedStock1.Quantity}, Deleted (unchanged): {updatedStock2.Quantity}");
    }

    [Fact]
    public async Task ReduceStockLevels_EmptySale_ReturnsSuccessWithZeroUpdates()
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            UserId = _userId,
            DeviceId = _deviceId,
            InvoiceNumber = "INV-EMPTY-001",
            TotalAmount = 0,
            Status = SaleStatus.Completed,
            Items = new List<SaleItem>(),
            AppliedDiscounts = new List<SaleDiscount>()
        };

        var result = await _inventoryUpdater.ReduceStockLevelsAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ItemsUpdated);

        _output.WriteLine("Empty sale handled gracefully");
    }

    [Fact]
    public async Task ReduceStockLevels_NullSale_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _inventoryUpdater.ReduceStockLevelsAsync(null!));
    }

    [Fact]
    public async Task ReduceStockLevels_StockFloorAtZero_NeverGoesNegative()
    {
        // Stock has only 2 units but sale requests 10
        var (product, stock) = await CreateProductWithStockAsync("Scarce Product", 2);
        var sale = BuildSaleWithItems(new() { (product, 10, false) });

        var result = await _inventoryUpdater.ReduceStockLevelsAsync(sale);

        Assert.True(result.IsSuccess);

        var updatedStock = await _context.Stock.FindAsync(stock.Id);
        Assert.True(updatedStock!.Quantity >= 0, "Stock should never go negative");

        _output.WriteLine($"Stock floored at: {updatedStock.Quantity} (never negative)");
    }

    [Fact]
    public async Task ReduceStockLevels_UpdateDetailsContainCorrectBeforeAfterValues()
    {
        var (product, stock) = await CreateProductWithStockAsync("Tracked Product", 25);
        var sale = BuildSaleWithItems(new() { (product, 8, false) });

        var result = await _inventoryUpdater.ReduceStockLevelsAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.Single(result.UpdatedItems);

        var detail = result.UpdatedItems[0];
        Assert.Equal(product.Id, detail.ProductId);
        Assert.Equal(8, detail.QuantityDeducted);
        Assert.Equal(25, detail.StockBefore);
        Assert.Equal(17, detail.StockAfter);

        _output.WriteLine($"Detail: before={detail.StockBefore}, deducted={detail.QuantityDeducted}, after={detail.StockAfter}");
    }

    // =========================================================================
    // UpdateCustomerPurchaseHistoryAsync Tests (Req 6.6)
    // =========================================================================

    [Fact]
    public async Task UpdateCustomerPurchaseHistory_WithCustomer_UpdatesTotalSpentAndVisitCount()
    {
        var customer = await CreateCustomerAsync("Alice Smith", totalSpent: 100m);
        var (product, _) = await CreateProductWithStockAsync("Product", 10);
        var sale = BuildSaleWithItems(new() { (product, 2, false) }, customer);
        sale.TotalAmount = 50m;

        var result = await _inventoryUpdater.UpdateCustomerPurchaseHistoryAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.Equal(customer.Id, result.CustomerId);
        Assert.Equal(50m, result.AmountAdded);

        // Verify customer was updated in DB
        var updatedCustomer = await _context.Customers.FindAsync(customer.Id);
        Assert.Equal(150m, updatedCustomer!.TotalSpent); // 100 + 50
        Assert.Equal(1, updatedCustomer.VisitCount);
        Assert.NotNull(updatedCustomer.LastVisit);

        _output.WriteLine($"Customer TotalSpent: 100 → {updatedCustomer.TotalSpent}, VisitCount: {updatedCustomer.VisitCount}");
    }

    [Fact]
    public async Task UpdateCustomerPurchaseHistory_NoCustomer_ReturnsNoCustomerResult()
    {
        var (product, _) = await CreateProductWithStockAsync("Product", 10);
        var sale = BuildSaleWithItems(new() { (product, 1, false) }); // No customer
        sale.CustomerId = null;
        sale.Customer = null;

        var result = await _inventoryUpdater.UpdateCustomerPurchaseHistoryAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.True(result.NoCustomer);
        Assert.Null(result.CustomerId);

        _output.WriteLine("No-customer sale handled correctly");
    }

    [Fact]
    public async Task UpdateCustomerPurchaseHistory_TierUpgrade_DetectedAndReported()
    {
        // Customer at 900 spent (just below Silver threshold of 1000)
        var customer = await CreateCustomerAsync("Bob Jones", totalSpent: 900m, tier: MembershipTier.Bronze);
        var (product, _) = await CreateProductWithStockAsync("Product", 10);
        var sale = BuildSaleWithItems(new() { (product, 1, false) }, customer);
        sale.TotalAmount = 200m; // 900 + 200 = 1100 → should upgrade to Silver

        var result = await _inventoryUpdater.UpdateCustomerPurchaseHistoryAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.True(result.TierUpgraded);
        Assert.Equal("Silver", result.NewTier);

        _output.WriteLine($"Tier upgraded: Bronze → {result.NewTier}");
    }

    [Fact]
    public async Task UpdateCustomerPurchaseHistory_NoTierUpgrade_TierUpgradedIsFalse()
    {
        var customer = await CreateCustomerAsync("Carol White", totalSpent: 50m, tier: MembershipTier.Bronze);
        var (product, _) = await CreateProductWithStockAsync("Product", 10);
        var sale = BuildSaleWithItems(new() { (product, 1, false) }, customer);
        sale.TotalAmount = 20m; // 50 + 20 = 70 → stays Bronze

        var result = await _inventoryUpdater.UpdateCustomerPurchaseHistoryAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.False(result.TierUpgraded);
        Assert.Null(result.NewTier);

        _output.WriteLine("No tier upgrade correctly reported");
    }

    [Fact]
    public async Task UpdateCustomerPurchaseHistory_WithMembership_UpdatesPoints()
    {
        var customer = await CreateCustomerAsync("Dave Brown", totalSpent: 0m);

        // Create a CustomerMembership record for this customer
        var membership = new CustomerMembership
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Tier = MembershipTier.Bronze,
            Points = 100,
            IsActive = true,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.NotSynced
        };
        _context.CustomerMemberships.Add(membership);
        await _context.SaveChangesAsync();

        var (product, _) = await CreateProductWithStockAsync("Product", 10);
        var sale = BuildSaleWithItems(new() { (product, 1, false) }, customer);
        sale.TotalAmount = 75m; // Should earn 75 points

        var result = await _inventoryUpdater.UpdateCustomerPurchaseHistoryAsync(sale);

        Assert.True(result.IsSuccess);

        var updatedMembership = await _context.CustomerMemberships.FindAsync(membership.Id);
        Assert.Equal(175, updatedMembership!.Points); // 100 + 75

        _output.WriteLine($"Membership points: 100 → {updatedMembership.Points}");
    }

    [Fact]
    public async Task UpdateCustomerPurchaseHistory_NullSale_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _inventoryUpdater.UpdateCustomerPurchaseHistoryAsync(null!));
    }

    // =========================================================================
    // RollbackInventoryUpdateAsync Tests
    // =========================================================================

    [Fact]
    public async Task RollbackInventoryUpdate_AfterStockReduction_RestoresStockLevels()
    {
        var (product, stock) = await CreateProductWithStockAsync("Rollback Product", 20);
        var sale = BuildSaleWithItems(new() { (product, 5, false) });

        // First reduce stock
        var reduceResult = await _inventoryUpdater.ReduceStockLevelsAsync(sale);
        Assert.True(reduceResult.IsSuccess);

        var stockAfterReduction = await _context.Stock.FindAsync(stock.Id);
        Assert.Equal(15, stockAfterReduction!.Quantity); // 20 - 5 = 15

        // Now rollback
        var rollbackResult = await _inventoryUpdater.RollbackInventoryUpdateAsync(sale.Id);

        Assert.True(rollbackResult.IsSuccess);
        Assert.Equal(1, rollbackResult.ItemsRestored);

        var stockAfterRollback = await _context.Stock.FindAsync(stock.Id);
        Assert.Equal(20, stockAfterRollback!.Quantity); // Restored to 20

        _output.WriteLine($"Rollback: 15 → {stockAfterRollback.Quantity} (restored to original 20)");
    }

    [Fact]
    public async Task RollbackInventoryUpdate_MultipleItems_RestoresAllItems()
    {
        var (product1, stock1) = await CreateProductWithStockAsync("Rollback A", 10);
        var (product2, stock2) = await CreateProductWithStockAsync("Rollback B", 20);
        var sale = BuildSaleWithItems(new()
        {
            (product1, 3, false),
            (product2, 8, false)
        });

        await _inventoryUpdater.ReduceStockLevelsAsync(sale);

        var rollbackResult = await _inventoryUpdater.RollbackInventoryUpdateAsync(sale.Id);

        Assert.True(rollbackResult.IsSuccess);
        Assert.Equal(2, rollbackResult.ItemsRestored);

        var updatedStock1 = await _context.Stock.FindAsync(stock1.Id);
        var updatedStock2 = await _context.Stock.FindAsync(stock2.Id);

        Assert.Equal(10, updatedStock1!.Quantity); // Restored to original
        Assert.Equal(20, updatedStock2!.Quantity); // Restored to original

        _output.WriteLine($"Rollback restored: A={updatedStock1.Quantity}, B={updatedStock2.Quantity}");
    }

    [Fact]
    public async Task RollbackInventoryUpdate_EmptySaleId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _inventoryUpdater.RollbackInventoryUpdateAsync(Guid.Empty));
    }

    [Fact]
    public async Task RollbackInventoryUpdate_NoSnapshotsExist_FallsBackToDbRollback()
    {
        // Create a sale in the DB with items (simulating a restart scenario)
        var (product, stock) = await CreateProductWithStockAsync("DB Rollback Product", 10);

        var saleId = Guid.NewGuid();
        var saleItem = new SaleItem
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ProductId = product.Id,
            Quantity = 3,
            UnitPrice = 10m,
            TotalPrice = 30m,
            IsDeleted = false
        };

        var sale = new Sale
        {
            Id = saleId,
            ShopId = _shopId,
            UserId = _userId,
            DeviceId = _deviceId,
            InvoiceNumber = "INV-DBROLLBACK-001",
            TotalAmount = 30m,
            Status = SaleStatus.Completed,
            Items = new List<SaleItem> { saleItem },
            AppliedDiscounts = new List<SaleDiscount>()
        };
        _context.Sales.Add(sale);
        _context.SaleItems.Add(saleItem);
        await _context.SaveChangesAsync();

        // Rollback without prior ReduceStockLevelsAsync (no in-memory snapshots)
        var rollbackResult = await _inventoryUpdater.RollbackInventoryUpdateAsync(saleId);

        // DB-based rollback should succeed (restores by adding back the quantity)
        Assert.True(rollbackResult.IsSuccess);

        _output.WriteLine($"DB-based rollback succeeded: {rollbackResult.ItemsRestored} items restored");
    }

    // =========================================================================
    // ProcessSaleInventoryUpdateAsync Tests (Combined Req 6.3 + 6.6)
    // =========================================================================

    [Fact]
    public async Task ProcessSaleInventoryUpdate_WithCustomer_ReducesStockAndUpdatesHistory()
    {
        var (product, stock) = await CreateProductWithStockAsync("Full Process Product", 30);
        var customer = await CreateCustomerAsync("Eve Davis", totalSpent: 200m);
        var sale = BuildSaleWithItems(new() { (product, 4, false) }, customer);
        sale.TotalAmount = 40m;

        var result = await _inventoryUpdater.ProcessSaleInventoryUpdateAsync(sale);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ItemsUpdated);

        // Verify stock was reduced
        var updatedStock = await _context.Stock.FindAsync(stock.Id);
        Assert.Equal(26, updatedStock!.Quantity); // 30 - 4 = 26

        // Verify customer history was updated
        var updatedCustomer = await _context.Customers.FindAsync(customer.Id);
        Assert.Equal(240m, updatedCustomer!.TotalSpent); // 200 + 40

        _output.WriteLine($"Full process: stock={updatedStock.Quantity}, customer spent={updatedCustomer.TotalSpent}");
    }

    [Fact]
    public async Task ProcessSaleInventoryUpdate_WithoutCustomer_ReducesStockOnly()
    {
        var (product, stock) = await CreateProductWithStockAsync("No Customer Product", 15);
        var sale = BuildSaleWithItems(new() { (product, 3, false) }); // No customer
        sale.CustomerId = null;
        sale.Customer = null;

        var result = await _inventoryUpdater.ProcessSaleInventoryUpdateAsync(sale);

        Assert.True(result.IsSuccess);

        var updatedStock = await _context.Stock.FindAsync(stock.Id);
        Assert.Equal(12, updatedStock!.Quantity); // 15 - 3 = 12

        _output.WriteLine($"No-customer sale: stock reduced to {updatedStock.Quantity}");
    }

    [Fact]
    public async Task ProcessSaleInventoryUpdate_NullSale_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _inventoryUpdater.ProcessSaleInventoryUpdateAsync(null!));
    }

    // =========================================================================
    // Concurrent Inventory Management Tests
    // =========================================================================

    [Fact]
    public async Task ReduceStockLevels_ConcurrentSales_AllUpdatesApplied()
    {
        // Create a product with enough stock for multiple concurrent sales
        var (product, stock) = await CreateProductWithStockAsync("Concurrent Product", 100);

        // Build 5 concurrent sales, each reducing by 5 units
        var sales = Enumerable.Range(0, 5).Select(_ =>
            BuildSaleWithItems(new() { (product, 5, false) })).ToList();

        // Run all reductions concurrently
        var tasks = sales.Select(s => _inventoryUpdater.ReduceStockLevelsAsync(s));
        var results = await Task.WhenAll(tasks);

        // All should succeed
        Assert.All(results, r => Assert.True(r.IsSuccess));

        // Stock should be reduced by 5 * 5 = 25 total
        var updatedStock = await _context.Stock.FindAsync(stock.Id);
        Assert.Equal(75, updatedStock!.Quantity); // 100 - 25 = 75

        _output.WriteLine($"Concurrent updates: 100 → {updatedStock.Quantity} (5 sales × 5 units)");
    }

    [Fact]
    public async Task ReduceStockLevels_ConcurrentSalesForDifferentProducts_AllSucceed()
    {
        // Create separate products for each concurrent sale
        var products = new List<(Product product, Stock stock)>();
        for (int i = 0; i < 3; i++)
        {
            products.Add(await CreateProductWithStockAsync($"Concurrent Product {i}", 20));
        }

        var sales = products.Select(p =>
            BuildSaleWithItems(new() { (p.product, 3, false) })).ToList();

        var tasks = sales.Select(s => _inventoryUpdater.ReduceStockLevelsAsync(s));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsSuccess));

        foreach (var (product, stock) in products)
        {
            var updatedStock = await _context.Stock.FindAsync(stock.Id);
            Assert.Equal(17, updatedStock!.Quantity); // 20 - 3 = 17
        }

        _output.WriteLine("All concurrent sales for different products succeeded");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
