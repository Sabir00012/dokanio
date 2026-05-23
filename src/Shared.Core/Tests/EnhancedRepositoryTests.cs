using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Repositories;
using Shared.Core.Services;
using Xunit;

namespace Shared.Core.Tests;

/// <summary>
/// Integration tests for enhanced repository implementations.
/// Validates transaction support, efficient query methods, and caching integration
/// for Sale, SaleItem, Product, and Stock repositories.
/// Requirements: 9.3, 9.4
/// </summary>
public class EnhancedRepositoryTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PosDbContext _context;
    private readonly ISaleRepository _saleRepo;
    private readonly IProductRepository _productRepo;
    private readonly IStockRepository _stockRepo;
    private readonly ISalesCacheService _cache;

    private readonly Guid _shopId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public EnhancedRepositoryTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<PosDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString())
             .ConfigureWarnings(w => w.Ignore(
                 Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddLogging();
        services.AddMemoryCache();
        services.AddScoped<ISalesCacheService, SalesCacheService>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IStockRepository, StockRepository>();

        _sp = services.BuildServiceProvider();
        _context = _sp.GetRequiredService<PosDbContext>();
        _saleRepo = _sp.GetRequiredService<ISaleRepository>();
        _productRepo = _sp.GetRequiredService<IProductRepository>();
        _stockRepo = _sp.GetRequiredService<IStockRepository>();
        _cache = _sp.GetRequiredService<ISalesCacheService>();
    }

    // ── Transaction support (Requirement 9.3) ─────────────────────────────────

    [Fact]
    public async Task BeginTransactionAsync_ReturnsTransaction()
    {
        // In-memory DB ignores transactions but the method should not throw
        await using var tx = await _saleRepo.BeginTransactionAsync();
        Assert.NotNull(tx);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_CommitsOnSuccess()
    {
        var product = MakeProduct();
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // Execute a stock update inside a transaction
        await _stockRepo.ExecuteInTransactionAsync(async () =>
        {
            var stock = new Stock
            {
                Id = Guid.NewGuid(),
                ShopId = _shopId,
                ProductId = product.Id,
                Quantity = 100,
                DeviceId = _deviceId
            };
            await _stockRepo.AddAsync(stock);
            await _stockRepo.SaveChangesAsync();
        });

        var saved = await _stockRepo.GetByProductIdAsync(product.Id);
        Assert.NotNull(saved);
        Assert.Equal(100, saved!.Quantity);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WithResult_ReturnsValue()
    {
        var product = MakeProduct();
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var result = await _productRepo.ExecuteInTransactionAsync(async () =>
        {
            var found = await _productRepo.GetByIdAsync(product.Id);
            return found?.Name ?? "not found";
        });

        Assert.Equal(product.Name, result);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RollsBackOnException()
    {
        // In-memory DB doesn't truly roll back, but the method should propagate the exception
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _saleRepo.ExecuteInTransactionAsync(async () =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("Simulated failure");
            });
        });
    }

    // ── Sale enhanced queries (Requirement 9.3) ────────────────────────────────

    [Fact]
    public async Task GetActiveSalesAsync_ReturnsOnlyDraftAndActiveSales()
    {
        await SeedSalesAsync(3, SaleStatus.Active);
        await SeedSalesAsync(2, SaleStatus.Draft);
        await SeedSalesAsync(4, SaleStatus.Completed);
        await SeedSalesAsync(1, SaleStatus.Cancelled);

        var activeSales = await _saleRepo.GetActiveSalesAsync();

        Assert.Equal(5, activeSales.Count());
        Assert.All(activeSales, s =>
            Assert.True(s.Status == SaleStatus.Active || s.Status == SaleStatus.Draft));
    }

    [Fact]
    public async Task GetActiveSalesAsync_FiltersByShop()
    {
        var otherShopId = Guid.NewGuid();
        await SeedSalesAsync(3, SaleStatus.Active, shopId: _shopId);
        await SeedSalesAsync(2, SaleStatus.Active, shopId: otherShopId);

        var result = await _saleRepo.GetActiveSalesAsync(shopId: _shopId);

        Assert.Equal(3, result.Count());
        Assert.All(result, s => Assert.Equal(_shopId, s.ShopId));
    }

    [Fact]
    public async Task GetActiveSalesAsync_EmptyDatabase_ReturnsEmpty()
    {
        var result = await _saleRepo.GetActiveSalesAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSalesByCustomerAsync_ReturnsOnlyCompletedSalesForCustomer()
    {
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();

        await SeedSalesAsync(3, SaleStatus.Completed, customerId: customerId);
        await SeedSalesAsync(2, SaleStatus.Active, customerId: customerId); // active - should be excluded
        await SeedSalesAsync(2, SaleStatus.Completed, customerId: otherCustomerId);

        var result = await _saleRepo.GetSalesByCustomerAsync(customerId);

        Assert.Equal(3, result.Count());
        Assert.All(result, s =>
        {
            Assert.Equal(customerId, s.CustomerId);
            Assert.Equal(SaleStatus.Completed, s.Status);
        });
    }

    [Fact]
    public async Task GetSalesByCustomerAsync_RespectsLimit()
    {
        var customerId = Guid.NewGuid();
        await SeedSalesAsync(10, SaleStatus.Completed, customerId: customerId);

        var result = await _saleRepo.GetSalesByCustomerAsync(customerId, limit: 5);

        Assert.Equal(5, result.Count());
    }

    [Fact]
    public async Task GetDailySalesAmountAsync_SumsCompletedSalesForDate()
    {
        var today = DateTime.UtcNow;
        await SeedSalesWithAmountAsync(3, SaleStatus.Completed, 100m, today);
        await SeedSalesWithAmountAsync(2, SaleStatus.Active, 50m, today); // active - should be excluded

        var total = await _saleRepo.GetDailySalesAmountAsync(today);

        Assert.Equal(300m, total); // 3 × 100
    }

    [Fact]
    public async Task GetDailySalesAmountAsync_FiltersByShop()
    {
        var otherShopId = Guid.NewGuid();
        var today = DateTime.UtcNow;
        await SeedSalesWithAmountAsync(2, SaleStatus.Completed, 100m, today, shopId: _shopId);
        await SeedSalesWithAmountAsync(3, SaleStatus.Completed, 100m, today, shopId: otherShopId);

        var total = await _saleRepo.GetDailySalesAmountAsync(today, shopId: _shopId);

        Assert.Equal(200m, total);
    }

    [Fact]
    public async Task GetDailySalesCountAsync_CountsOnlyCompletedSalesForDate()
    {
        var today = DateTime.UtcNow;
        await SeedSalesAsync(4, SaleStatus.Completed, createdAt: today);
        await SeedSalesAsync(2, SaleStatus.Active, createdAt: today);

        var count = await _saleRepo.GetDailySalesCountAsync(today);

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task GetDailySalesCountAsync_FiltersByShop()
    {
        var otherShopId = Guid.NewGuid();
        var today = DateTime.UtcNow;
        await SeedSalesAsync(3, SaleStatus.Completed, shopId: _shopId, createdAt: today);
        await SeedSalesAsync(5, SaleStatus.Completed, shopId: otherShopId, createdAt: today);

        var count = await _saleRepo.GetDailySalesCountAsync(today, shopId: _shopId);

        Assert.Equal(3, count);
    }

    // ── Product enhanced queries (Requirement 9.3, 9.4) ───────────────────────

    [Fact]
    public async Task GetProductWithStockAsync_ReturnsProductWithStockEntries()
    {
        var product = MakeProduct();
        _context.Products.Add(product);
        _context.Stock.Add(new Stock
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            ProductId = product.Id,
            Quantity = 50,
            DeviceId = _deviceId
        });
        await _context.SaveChangesAsync();

        var result = await _productRepo.GetProductWithStockAsync(product.Id);

        Assert.NotNull(result);
        Assert.Equal(product.Id, result!.Id);
        Assert.Single(result.StockEntries);
        Assert.Equal(50, result.StockEntries.First().Quantity);
    }

    [Fact]
    public async Task GetProductWithStockAsync_NonExistentProduct_ReturnsNull()
    {
        var result = await _productRepo.GetProductWithStockAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetProductsByIdsAsync_ReturnsBatchOfProducts()
    {
        var products = Enumerable.Range(0, 5).Select(_ => MakeProduct()).ToList();
        _context.Products.AddRange(products);
        await _context.SaveChangesAsync();

        var ids = products.Select(p => p.Id).ToList();
        var result = await _productRepo.GetProductsByIdsAsync(ids);

        Assert.Equal(5, result.Count);
        Assert.All(ids, id => Assert.True(result.ContainsKey(id)));
    }

    [Fact]
    public async Task GetProductsByIdsAsync_EmptyInput_ReturnsEmptyDictionary()
    {
        var result = await _productRepo.GetProductsByIdsAsync(Enumerable.Empty<Guid>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProductsByIdsAsync_MissingIds_OnlyReturnsFoundProducts()
    {
        var product = MakeProduct();
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var ids = new[] { product.Id, Guid.NewGuid(), Guid.NewGuid() };
        var result = await _productRepo.GetProductsByIdsAsync(ids);

        Assert.Single(result);
        Assert.True(result.ContainsKey(product.Id));
    }

    [Fact]
    public async Task GetWeightBasedProductsAsync_ReturnsOnlyWeightBasedProducts()
    {
        var weightBased = MakeProduct(isWeightBased: true);
        var regular = MakeProduct(isWeightBased: false);
        _context.Products.AddRange(weightBased, regular);
        await _context.SaveChangesAsync();

        var result = await _productRepo.GetWeightBasedProductsAsync(_shopId);

        Assert.Single(result);
        Assert.Equal(weightBased.Id, result.First().Id);
        Assert.True(result.First().IsWeightBased);
    }

    [Fact]
    public async Task GetWeightBasedProductsAsync_ExcludesInactiveProducts()
    {
        var active = MakeProduct(isWeightBased: true, isActive: true);
        var inactive = MakeProduct(isWeightBased: true, isActive: false);
        _context.Products.AddRange(active, inactive);
        await _context.SaveChangesAsync();

        var result = await _productRepo.GetWeightBasedProductsAsync(_shopId);

        Assert.Single(result);
        Assert.Equal(active.Id, result.First().Id);
    }

    // ── Product cache integration (Requirement 9.4) ────────────────────────────

    [Fact]
    public async Task GetByBarcodeAsync_CachesResultOnFirstFetch()
    {
        var product = MakeProduct(barcode: "CACHE-TEST-001");
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // First call - cache miss, hits DB
        var first = await _productRepo.GetByBarcodeAsync("CACHE-TEST-001");
        Assert.NotNull(first);

        // Second call - should be served from cache
        var second = await _productRepo.GetByBarcodeAsync("CACHE-TEST-001");
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
    }

    [Fact]
    public async Task GetProductsByIdsAsync_UsesCacheForSubsequentCalls()
    {
        var product = MakeProduct();
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // First batch fetch - populates cache
        await _productRepo.GetProductsByIdsAsync(new[] { product.Id });

        // Verify product is now in cache
        var cached = await _cache.GetProductByIdAsync(product.Id);
        Assert.NotNull(cached);
        Assert.Equal(product.Id, cached!.Id);
    }

    // ── Stock enhanced queries (Requirement 9.3) ──────────────────────────────

    [Fact]
    public async Task GetStockQuantitiesBatchAsync_ReturnsBatchQuantities()
    {
        var products = Enumerable.Range(0, 3).Select(_ => MakeProduct()).ToList();
        _context.Products.AddRange(products);
        await _context.SaveChangesAsync();

        for (int i = 0; i < products.Count; i++)
        {
            _context.Stock.Add(new Stock
            {
                Id = Guid.NewGuid(),
                ShopId = _shopId,
                ProductId = products[i].Id,
                Quantity = (i + 1) * 10,
                DeviceId = _deviceId
            });
        }
        await _context.SaveChangesAsync();

        var ids = products.Select(p => p.Id).ToList();
        var result = await _stockRepo.GetStockQuantitiesBatchAsync(ids);

        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[products[0].Id]);
        Assert.Equal(20, result[products[1].Id]);
        Assert.Equal(30, result[products[2].Id]);
    }

    [Fact]
    public async Task GetStockQuantitiesBatchAsync_MissingProducts_DefaultToZero()
    {
        var missingId = Guid.NewGuid();
        var result = await _stockRepo.GetStockQuantitiesBatchAsync(new[] { missingId });

        Assert.Single(result);
        Assert.Equal(0, result[missingId]);
    }

    [Fact]
    public async Task GetStockQuantitiesBatchAsync_EmptyInput_ReturnsEmpty()
    {
        var result = await _stockRepo.GetStockQuantitiesBatchAsync(Enumerable.Empty<Guid>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task DeductStockBatchAsync_ReducesStockForAllProducts()
    {
        var product1 = MakeProduct();
        var product2 = MakeProduct();
        _context.Products.AddRange(product1, product2);
        _context.Stock.AddRange(
            new Stock { Id = Guid.NewGuid(), ShopId = _shopId, ProductId = product1.Id, Quantity = 100, DeviceId = _deviceId },
            new Stock { Id = Guid.NewGuid(), ShopId = _shopId, ProductId = product2.Id, Quantity = 50, DeviceId = _deviceId }
        );
        await _context.SaveChangesAsync();

        var reductions = new Dictionary<Guid, int>
        {
            { product1.Id, 10 },
            { product2.Id, 5 }
        };

        await _stockRepo.DeductStockBatchAsync(reductions, _deviceId);
        await _stockRepo.SaveChangesAsync();

        Assert.Equal(90, await _stockRepo.GetStockQuantityAsync(product1.Id));
        Assert.Equal(45, await _stockRepo.GetStockQuantityAsync(product2.Id));
    }

    [Fact]
    public async Task DeductStockBatchAsync_InsufficientStock_ThrowsInvalidOperationException()
    {
        var product = MakeProduct();
        _context.Products.Add(product);
        _context.Stock.Add(new Stock
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            ProductId = product.Id,
            Quantity = 5,
            DeviceId = _deviceId
        });
        await _context.SaveChangesAsync();

        var reductions = new Dictionary<Guid, int> { { product.Id, 10 } }; // more than available

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _stockRepo.DeductStockBatchAsync(reductions, _deviceId));
    }

    [Fact]
    public async Task DeductStockBatchAsync_MissingStockRecord_ThrowsInvalidOperationException()
    {
        var reductions = new Dictionary<Guid, int> { { Guid.NewGuid(), 1 } };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _stockRepo.DeductStockBatchAsync(reductions, _deviceId));
    }

    [Fact]
    public async Task DeductStockBatchAsync_EmptyReductions_DoesNotThrow()
    {
        // Should be a no-op
        await _stockRepo.DeductStockBatchAsync(new Dictionary<Guid, int>(), _deviceId);
    }

    [Fact]
    public async Task DeductStockBatchAsync_MarksStockAsNotSynced()
    {
        var product = MakeProduct();
        _context.Products.Add(product);
        _context.Stock.Add(new Stock
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            ProductId = product.Id,
            Quantity = 100,
            DeviceId = _deviceId,
            SyncStatus = SyncStatus.Synced
        });
        await _context.SaveChangesAsync();

        await _stockRepo.DeductStockBatchAsync(new Dictionary<Guid, int> { { product.Id, 1 } }, _deviceId);
        await _stockRepo.SaveChangesAsync();

        var stock = await _stockRepo.GetByProductIdAsync(product.Id);
        Assert.Equal(SyncStatus.NotSynced, stock!.SyncStatus);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Product MakeProduct(string? barcode = null, bool isWeightBased = false, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        ShopId = _shopId,
        Name = $"Product-{Guid.NewGuid():N}",
        Barcode = barcode,
        UnitPrice = 10m,
        IsActive = isActive,
        IsWeightBased = isWeightBased,
        RatePerKilogram = isWeightBased ? 5m : null,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        DeviceId = _deviceId
    };

    private async Task SeedSalesAsync(
        int count,
        SaleStatus status,
        Guid? shopId = null,
        Guid? customerId = null,
        DateTime? createdAt = null)
    {
        var sid = shopId ?? _shopId;
        var at = createdAt ?? DateTime.UtcNow;

        for (int i = 0; i < count; i++)
        {
            _context.Sales.Add(new Sale
            {
                Id = Guid.NewGuid(),
                ShopId = sid,
                UserId = _userId,
                InvoiceNumber = $"INV-{Guid.NewGuid():N}",
                Status = status,
                TotalAmount = 100m,
                FinalTotal = 100m,
                CustomerId = customerId,
                CreatedAt = at,
                UpdatedAt = at,
                DeviceId = _deviceId
            });
        }
        await _context.SaveChangesAsync();
    }

    private async Task SeedSalesWithAmountAsync(
        int count,
        SaleStatus status,
        decimal amount,
        DateTime createdAt,
        Guid? shopId = null)
    {
        var sid = shopId ?? _shopId;

        for (int i = 0; i < count; i++)
        {
            _context.Sales.Add(new Sale
            {
                Id = Guid.NewGuid(),
                ShopId = sid,
                UserId = _userId,
                InvoiceNumber = $"INV-{Guid.NewGuid():N}",
                Status = status,
                TotalAmount = amount,
                FinalTotal = amount,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                DeviceId = _deviceId
            });
        }
        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        _sp.Dispose();
    }
}
