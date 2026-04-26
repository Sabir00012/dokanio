using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shared.Core.Entities;

namespace Shared.Core.Services;

/// <summary>
/// IMemoryCache-backed implementation of <see cref="ISalesCacheService"/>.
/// TTLs are intentionally short so stale data is never served for long in a
/// high-throughput POS environment.
/// </summary>
public class SalesCacheService : ISalesCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<SalesCacheService> _logger;

    // Cache key prefixes
    private const string ProductByIdPrefix    = "sales:product:id:";
    private const string ProductByBarcodePrefix = "sales:product:barcode:";
    private const string TaxRatePrefix        = "sales:taxrate:shop:";
    private const string ActiveSalePrefix     = "sales:active:";

    // TTLs
    private static readonly TimeSpan ProductTtl    = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TaxRateTtl    = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ActiveSaleTtl = TimeSpan.FromMinutes(5);

    public SalesCacheService(IMemoryCache cache, ILogger<SalesCacheService> logger)
    {
        _cache  = cache  ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Product cache ──────────────────────────────────────────────────────────

    public Task<Product?> GetProductByIdAsync(Guid productId)
    {
        var key = ProductByIdPrefix + productId;
        _cache.TryGetValue(key, out Product? product);
        _logger.LogDebug("Product cache {Result} for id={ProductId}", product is null ? "MISS" : "HIT", productId);
        return Task.FromResult(product);
    }

    public Task SetProductAsync(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        var key = ProductByIdPrefix + product.Id;
        _cache.Set(key, product, ProductTtl);
        _logger.LogDebug("Cached product id={ProductId} (TTL={Ttl})", product.Id, ProductTtl);
        return Task.CompletedTask;
    }

    public Task<Product?> GetProductByBarcodeAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return Task.FromResult<Product?>(null);

        var key = ProductByBarcodePrefix + barcode;
        _cache.TryGetValue(key, out Product? product);
        _logger.LogDebug("Product cache {Result} for barcode={Barcode}", product is null ? "MISS" : "HIT", barcode);
        return Task.FromResult(product);
    }

    public Task SetProductByBarcodeAsync(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (string.IsNullOrWhiteSpace(product.Barcode))
            return Task.CompletedTask;

        var key = ProductByBarcodePrefix + product.Barcode;
        _cache.Set(key, product, ProductTtl);
        _logger.LogDebug("Cached product barcode={Barcode} (TTL={Ttl})", product.Barcode, ProductTtl);
        return Task.CompletedTask;
    }

    public Task InvalidateProductAsync(Guid productId, string? barcode = null)
    {
        _cache.Remove(ProductByIdPrefix + productId);
        _logger.LogDebug("Invalidated product cache for id={ProductId}", productId);

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            _cache.Remove(ProductByBarcodePrefix + barcode);
            _logger.LogDebug("Invalidated product cache for barcode={Barcode}", barcode);
        }

        return Task.CompletedTask;
    }

    // ── Tax / shop configuration cache ────────────────────────────────────────

    public Task<decimal?> GetTaxRateAsync(Guid shopId)
    {
        var key = TaxRatePrefix + shopId;
        decimal? rate = _cache.TryGetValue(key, out decimal cached) ? cached : null;
        _logger.LogDebug("Tax rate cache {Result} for shopId={ShopId}", rate is null ? "MISS" : "HIT", shopId);
        return Task.FromResult(rate);
    }

    public Task SetTaxRateAsync(Guid shopId, decimal taxRate)
    {
        var key = TaxRatePrefix + shopId;
        _cache.Set(key, taxRate, TaxRateTtl);
        _logger.LogDebug("Cached tax rate={TaxRate} for shopId={ShopId} (TTL={Ttl})", taxRate, shopId, TaxRateTtl);
        return Task.CompletedTask;
    }

    public Task InvalidateTaxRateAsync(Guid shopId)
    {
        _cache.Remove(TaxRatePrefix + shopId);
        _logger.LogDebug("Invalidated tax rate cache for shopId={ShopId}", shopId);
        return Task.CompletedTask;
    }

    // ── Active sale session cache ──────────────────────────────────────────────

    public Task<Sale?> GetActiveSaleAsync(Guid saleId)
    {
        var key = ActiveSalePrefix + saleId;
        _cache.TryGetValue(key, out Sale? sale);
        _logger.LogDebug("Active sale cache {Result} for saleId={SaleId}", sale is null ? "MISS" : "HIT", saleId);
        return Task.FromResult(sale);
    }

    public Task SetActiveSaleAsync(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        var key = ActiveSalePrefix + sale.Id;
        _cache.Set(key, sale, ActiveSaleTtl);
        _logger.LogDebug("Cached active sale saleId={SaleId} (TTL={Ttl})", sale.Id, ActiveSaleTtl);
        return Task.CompletedTask;
    }

    public Task InvalidateActiveSaleAsync(Guid saleId)
    {
        _cache.Remove(ActiveSalePrefix + saleId);
        _logger.LogDebug("Invalidated active sale cache for saleId={SaleId}", saleId);
        return Task.CompletedTask;
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    public Task ClearAllAsync()
    {
        // IMemoryCache does not expose a bulk-clear API; compact to 100 % forces eviction of
        // all entries that are eligible (size-limited caches) or we can use the compact trick.
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0);
        }
        _logger.LogInformation("Sales cache cleared");
        return Task.CompletedTask;
    }
}
