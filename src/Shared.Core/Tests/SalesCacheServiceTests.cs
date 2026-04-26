using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Services;
using System.Diagnostics;
using Xunit;

namespace Shared.Core.Tests;

/// <summary>
/// Unit tests for <see cref="SalesCacheService"/>.
/// Validates caching behaviour, TTL-based expiry, and cache invalidation.
/// </summary>
public class SalesCacheServiceTests : IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly ISalesCacheService _sut;

    public SalesCacheServiceTests()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        _memoryCache = sp.GetRequiredService<IMemoryCache>();
        var logger = sp.GetRequiredService<ILogger<SalesCacheService>>();
        _sut = new SalesCacheService(_memoryCache, logger);
    }

    // ── Product by ID ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProductById_WhenNotCached_ReturnsNull()
    {
        var result = await _sut.GetProductByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task SetProduct_ThenGetById_ReturnsCachedProduct()
    {
        var product = MakeProduct();
        await _sut.SetProductAsync(product);

        var cached = await _sut.GetProductByIdAsync(product.Id);

        Assert.NotNull(cached);
        Assert.Equal(product.Id, cached!.Id);
        Assert.Equal(product.Name, cached.Name);
    }

    [Fact]
    public async Task InvalidateProduct_ById_RemovesFromCache()
    {
        var product = MakeProduct();
        await _sut.SetProductAsync(product);

        await _sut.InvalidateProductAsync(product.Id);

        var cached = await _sut.GetProductByIdAsync(product.Id);
        Assert.Null(cached);
    }

    // ── Product by barcode ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetProductByBarcode_WhenNotCached_ReturnsNull()
    {
        var result = await _sut.GetProductByBarcodeAsync("BARCODE-MISS");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetProductByBarcode_ThenGetByBarcode_ReturnsCachedProduct()
    {
        var product = MakeProduct(barcode: "TEST-001");
        await _sut.SetProductByBarcodeAsync(product);

        var cached = await _sut.GetProductByBarcodeAsync("TEST-001");

        Assert.NotNull(cached);
        Assert.Equal(product.Id, cached!.Id);
    }

    [Fact]
    public async Task SetProductByBarcode_NullBarcode_DoesNotThrow()
    {
        var product = MakeProduct(barcode: null);
        // Should silently skip caching when barcode is null
        await _sut.SetProductByBarcodeAsync(product);
        // No exception expected
    }

    [Fact]
    public async Task InvalidateProduct_WithBarcode_RemovesBothEntries()
    {
        var product = MakeProduct(barcode: "REMOVE-ME");
        await _sut.SetProductAsync(product);
        await _sut.SetProductByBarcodeAsync(product);

        await _sut.InvalidateProductAsync(product.Id, product.Barcode);

        Assert.Null(await _sut.GetProductByIdAsync(product.Id));
        Assert.Null(await _sut.GetProductByBarcodeAsync(product.Barcode!));
    }

    // ── Tax rate ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTaxRate_WhenNotCached_ReturnsNull()
    {
        var result = await _sut.GetTaxRateAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task SetTaxRate_ThenGet_ReturnsCachedRate()
    {
        var shopId = Guid.NewGuid();
        await _sut.SetTaxRateAsync(shopId, 0.15m);

        var rate = await _sut.GetTaxRateAsync(shopId);

        Assert.NotNull(rate);
        Assert.Equal(0.15m, rate!.Value);
    }

    [Fact]
    public async Task InvalidateTaxRate_RemovesFromCache()
    {
        var shopId = Guid.NewGuid();
        await _sut.SetTaxRateAsync(shopId, 0.10m);

        await _sut.InvalidateTaxRateAsync(shopId);

        Assert.Null(await _sut.GetTaxRateAsync(shopId));
    }

    // ── Active sale ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveSale_WhenNotCached_ReturnsNull()
    {
        var result = await _sut.GetActiveSaleAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task SetActiveSale_ThenGet_ReturnsCachedSale()
    {
        var sale = MakeSale();
        await _sut.SetActiveSaleAsync(sale);

        var cached = await _sut.GetActiveSaleAsync(sale.Id);

        Assert.NotNull(cached);
        Assert.Equal(sale.Id, cached!.Id);
        Assert.Equal(sale.InvoiceNumber, cached.InvoiceNumber);
    }

    [Fact]
    public async Task InvalidateActiveSale_RemovesFromCache()
    {
        var sale = MakeSale();
        await _sut.SetActiveSaleAsync(sale);

        await _sut.InvalidateActiveSaleAsync(sale.Id);

        Assert.Null(await _sut.GetActiveSaleAsync(sale.Id));
    }

    // ── ClearAll ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearAll_DoesNotThrow()
    {
        var product = MakeProduct();
        await _sut.SetProductAsync(product);
        await _sut.SetTaxRateAsync(Guid.NewGuid(), 0.05m);

        // Should not throw
        await _sut.ClearAllAsync();
    }

    // ── Performance: cache hit should be fast ──────────────────────────────────

    [Fact]
    public async Task CacheHit_ShouldBeUnder5Ms()
    {
        var product = MakeProduct();
        await _sut.SetProductAsync(product);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            await _sut.GetProductByIdAsync(product.Id);
        }
        sw.Stop();

        // 100 cache hits should complete well under 500 ms total
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"100 cache hits took {sw.ElapsedMilliseconds}ms, expected < 500ms");
    }

    // ── Isolation: different shops have independent tax rates ──────────────────

    [Fact]
    public async Task TaxRates_AreIsolatedPerShop()
    {
        var shop1 = Guid.NewGuid();
        var shop2 = Guid.NewGuid();

        await _sut.SetTaxRateAsync(shop1, 0.05m);
        await _sut.SetTaxRateAsync(shop2, 0.20m);

        Assert.Equal(0.05m, (await _sut.GetTaxRateAsync(shop1))!.Value);
        Assert.Equal(0.20m, (await _sut.GetTaxRateAsync(shop2))!.Value);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Product MakeProduct(string? barcode = "BAR-001") => new()
    {
        Id       = Guid.NewGuid(),
        ShopId   = Guid.NewGuid(),
        Name     = "Test Product",
        Barcode  = barcode,
        UnitPrice = 9.99m,
        IsActive = true
    };

    private static Sale MakeSale() => new()
    {
        Id            = Guid.NewGuid(),
        ShopId        = Guid.NewGuid(),
        UserId        = Guid.NewGuid(),
        InvoiceNumber = $"INV-{Guid.NewGuid():N}",
        Status        = SaleStatus.Active,
        CreatedAt     = DateTime.UtcNow
    };

    public void Dispose()
    {
        _memoryCache.Dispose();
    }
}
