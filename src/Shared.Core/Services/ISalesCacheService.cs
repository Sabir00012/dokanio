using Shared.Core.Entities;

namespace Shared.Core.Services;

/// <summary>
/// Cache service for frequently accessed sales-related data.
/// Reduces database round-trips for hot-path operations (product lookups, tax config, active sessions).
/// </summary>
public interface ISalesCacheService
{
    // ── Product cache ──────────────────────────────────────────────────────────

    /// <summary>Gets a cached product by its primary key, or null on cache miss.</summary>
    Task<Product?> GetProductByIdAsync(Guid productId);

    /// <summary>Stores a product in the cache keyed by its primary key.</summary>
    Task SetProductAsync(Product product);

    /// <summary>Gets a cached product by barcode, or null on cache miss.</summary>
    Task<Product?> GetProductByBarcodeAsync(string barcode);

    /// <summary>Stores a product in the cache keyed by its barcode.</summary>
    Task SetProductByBarcodeAsync(Product product);

    /// <summary>Removes all cache entries for the given product (by ID and barcode).</summary>
    Task InvalidateProductAsync(Guid productId, string? barcode = null);

    // ── Tax / shop configuration cache ────────────────────────────────────────

    /// <summary>Gets the cached tax rate for a shop, or null on cache miss.</summary>
    Task<decimal?> GetTaxRateAsync(Guid shopId);

    /// <summary>Stores the tax rate for a shop.</summary>
    Task SetTaxRateAsync(Guid shopId, decimal taxRate);

    /// <summary>Removes the cached tax rate for a shop.</summary>
    Task InvalidateTaxRateAsync(Guid shopId);

    // ── Active sale session cache ──────────────────────────────────────────────

    /// <summary>Gets a cached active sale by its ID, or null on cache miss.</summary>
    Task<Sale?> GetActiveSaleAsync(Guid saleId);

    /// <summary>Stores an active sale in the cache.</summary>
    Task SetActiveSaleAsync(Sale sale);

    /// <summary>Removes the cached sale entry (call after completion or cancellation).</summary>
    Task InvalidateActiveSaleAsync(Guid saleId);

    // ── Utility ───────────────────────────────────────────────────────────────

    /// <summary>Removes all entries from the sales cache.</summary>
    Task ClearAllAsync();
}
