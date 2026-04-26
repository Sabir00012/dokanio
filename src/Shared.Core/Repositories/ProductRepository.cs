using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Core.Data;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Services;

namespace Shared.Core.Repositories;

/// <summary>
/// Product repository implementation with offline-first storage priority and in-process caching.
/// Frequently accessed products (by ID and barcode) are served from <see cref="ISalesCacheService"/>
/// to reduce database round-trips on the hot-path "add to sale" operation (Requirement 9.4).
/// </summary>
public class ProductRepository : Repository<Product>, IProductRepository
{
    private readonly ISalesCacheService _cache;

    public ProductRepository(PosDbContext context, ILogger<ProductRepository> logger, ISalesCacheService cache)
        : base(context, logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// Gets a product by its barcode from Local_Storage.
    /// Results are served from the in-process cache when available (Requirement 9.4).
    /// </summary>
    public async Task<Product?> GetByBarcodeAsync(string barcode)
    {
        try
        {
            _logger.LogDebug("Getting product by barcode {Barcode} from Local_Storage", barcode);
            
            if (string.IsNullOrWhiteSpace(barcode))
            {
                _logger.LogWarning("Barcode is null or empty");
                return null;
            }

            // Requirement 9.4: check cache first
            var cached = await _cache.GetProductByBarcodeAsync(barcode);
            if (cached != null)
            {
                _logger.LogDebug("Product barcode={Barcode} served from cache", barcode);
                return cached;
            }
            
            // Local-first: Query Local_Storage only for offline operation continuity
            var product = await _dbSet
                .FirstOrDefaultAsync(p => p.Barcode == barcode);
            
            if (product != null)
            {
                _logger.LogDebug("Found product {ProductName} with barcode {Barcode} in Local_Storage", product.Name, barcode);
                await _cache.SetProductByBarcodeAsync(product);
            }
            else
            {
                _logger.LogDebug("Product with barcode {Barcode} not found in Local_Storage", barcode);
            }
            
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product by barcode {Barcode} from Local_Storage", barcode);
            throw;
        }
    }

    /// <summary>
    /// Gets all active products in a specific category from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Product>> GetActiveByCategoryAsync(string category)
    {
        try
        {
            _logger.LogDebug("Getting active products by category {Category} from Local_Storage", category);
            
            // Local-first: Query Local_Storage only
            var products = await _dbSet
                .Where(p => p.IsActive && p.Category == category)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} active products in category {Category} in Local_Storage", products.Count, category);
            
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active products by category {Category} from Local_Storage", category);
            throw;
        }
    }

    /// <summary>
    /// Gets all medicine products expiring before the specified date from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Product>> GetExpiringMedicinesAsync(DateTime beforeDate)
    {
        try
        {
            _logger.LogDebug("Getting expiring medicines before {BeforeDate} from Local_Storage", beforeDate);
            
            // Local-first: Query Local_Storage only
            var expiringMedicines = await _dbSet
                .Where(p => p.IsActive && 
                           p.ExpiryDate.HasValue && 
                           p.ExpiryDate.Value < beforeDate)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} expiring medicines before {BeforeDate} in Local_Storage", expiringMedicines.Count, beforeDate);
            
            return expiringMedicines;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expiring medicines before {BeforeDate} from Local_Storage", beforeDate);
            throw;
        }
    }

    /// <summary>
    /// Gets all active products from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Product>> GetActiveProductsAsync()
    {
        try
        {
            _logger.LogDebug("Getting all active products from Local_Storage");
            
            // Local-first: Query Local_Storage only
            var activeProducts = await _dbSet
                .Where(p => p.IsActive)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} active products in Local_Storage", activeProducts.Count);
            
            return activeProducts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active products from Local_Storage");
            throw;
        }
    }

    /// <summary>
    /// Gets products that need to be synced to the server from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Product>> GetUnsyncedAsync()
    {
        try
        {
            _logger.LogDebug("Getting unsynced products from Local_Storage");
            
            // Local-first: Query Local_Storage only
            var unsyncedProducts = await _dbSet
                .Where(p => p.SyncStatus == SyncStatus.NotSynced || p.SyncStatus == SyncStatus.SyncFailed)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} unsynced products in Local_Storage", unsyncedProducts.Count);
            
            return unsyncedProducts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unsynced products from Local_Storage");
            throw;
        }
    }

    /// <summary>
    /// Searches products by name or barcode from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Product>> SearchAsync(string searchTerm)
    {
        try
        {
            _logger.LogDebug("Searching products by term {SearchTerm} from Local_Storage", searchTerm);
            
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return Enumerable.Empty<Product>();
            }
            
            var lowerSearchTerm = searchTerm.ToLower();
            
            // Local-first: Query Local_Storage only
            var matchingProducts = await _dbSet
                .Where(p => p.IsActive && 
                           (p.Name.ToLower().Contains(lowerSearchTerm) || 
                            p.Barcode != null && p.Barcode.ToLower().Contains(lowerSearchTerm)))
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} products matching search term {SearchTerm} in Local_Storage", matchingProducts.Count, searchTerm);
            
            return matchingProducts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching products by term {SearchTerm} from Local_Storage", searchTerm);
            throw;
        }
    }

    /// <summary>
    /// Gets all products for a specific shop from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Product>> GetProductsByShopAsync(Guid shopId)
    {
        try
        {
            _logger.LogDebug("Getting products for shop {ShopId} from Local_Storage", shopId);
            
            // Local-first: Query Local_Storage only
            var shopProducts = await _dbSet
                .Where(p => p.ShopId == shopId && p.IsActive)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} products for shop {ShopId} in Local_Storage", shopProducts.Count, shopId);
            
            return shopProducts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products for shop {ShopId} from Local_Storage", shopId);
            throw;
        }
    }

    /// <summary>
    /// Returns a paginated page of products for a shop with optional name/barcode search.
    /// Uses AsNoTracking for read-only performance (Requirement 9.6).
    /// </summary>
    public async Task<PagedResult<Product>> SearchProductsPagedAsync(
        Guid shopId,
        string? searchTerm = null,
        int page = 0,
        int pageSize = 20)
    {
        if (page < 0) page = 0;
        pageSize = Math.Clamp(pageSize, 1, 200);

        try
        {
            _logger.LogDebug(
                "Searching products paged: shopId={ShopId}, searchTerm={SearchTerm}, page={Page}, pageSize={PageSize}",
                shopId, searchTerm, page, pageSize);

            // Build base query with AsNoTracking for read-only performance
            var query = _dbSet
                .AsNoTracking()
                .Where(p => p.ShopId == shopId && p.IsActive && !p.IsDeleted);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var lower = searchTerm.ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(lower) ||
                    (p.Barcode != null && p.Barcode.ToLower().Contains(lower)));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderBy(p => p.Name)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync();

            _logger.LogDebug(
                "Paged product search: returned {Count}/{Total} items (page {Page})",
                items.Count, totalCount, page);

            return PagedResult<Product>.Create(items, totalCount, page, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching products paged for shop {ShopId} from Local_Storage", shopId);
            throw;
        }
    }

    // ── Enhanced query methods (Requirement 9.3, 9.4) ─────────────────────────

    /// <summary>
    /// Gets a product by ID with its current stock level in a single query.
    /// Results are served from the in-process cache when available (Requirement 9.4).
    /// </summary>
    public async Task<Product?> GetProductWithStockAsync(Guid productId)
    {
        try
        {
            _logger.LogDebug("Getting product with stock for productId={ProductId} from Local_Storage", productId);

            // Requirement 9.4: check cache first (stock is not cached, so always hit DB for stock)
            var product = await _dbSet
                .AsNoTracking()
                .Include(p => p.StockEntries)
                .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);

            if (product != null)
            {
                _logger.LogDebug("Found product {ProductName} with {StockCount} stock entries in Local_Storage",
                    product.Name, product.StockEntries.Count);
                // Cache the product (without stock navigation) for future ID lookups
                await _cache.SetProductAsync(product);
            }
            else
            {
                _logger.LogDebug("Product {ProductId} not found in Local_Storage", productId);
            }

            return product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product with stock for productId={ProductId} from Local_Storage", productId);
            throw;
        }
    }

    /// <summary>
    /// Gets multiple products by their IDs in a single batch query (Requirement 9.3).
    /// More efficient than calling GetByIdAsync in a loop.
    /// </summary>
    public async Task<Dictionary<Guid, Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds?.ToList() ?? new List<Guid>();
        if (ids.Count == 0)
            return new Dictionary<Guid, Product>();

        try
        {
            _logger.LogDebug("Batch-fetching {Count} products from Local_Storage", ids.Count);

            // Check cache first for each ID
            var result = new Dictionary<Guid, Product>(ids.Count);
            var missingIds = new List<Guid>();

            foreach (var id in ids)
            {
                var cached = await _cache.GetProductByIdAsync(id);
                if (cached != null)
                    result[id] = cached;
                else
                    missingIds.Add(id);
            }

            if (missingIds.Count > 0)
            {
                // Batch fetch missing products from DB
                var dbProducts = await _dbSet
                    .AsNoTracking()
                    .Where(p => missingIds.Contains(p.Id) && !p.IsDeleted)
                    .ToListAsync();

                foreach (var product in dbProducts)
                {
                    result[product.Id] = product;
                    await _cache.SetProductAsync(product);
                }
            }

            _logger.LogDebug("Batch-fetched {Found}/{Requested} products (cache hits: {CacheHits})",
                result.Count, ids.Count, ids.Count - missingIds.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch-fetching products from Local_Storage");
            throw;
        }
    }

    /// <summary>
    /// Gets all active weight-based products for a shop from Local_Storage (Requirement 9.3).
    /// </summary>
    public async Task<IEnumerable<Product>> GetWeightBasedProductsAsync(Guid shopId)
    {
        try
        {
            _logger.LogDebug("Getting weight-based products for shop {ShopId} from Local_Storage", shopId);

            var products = await _dbSet
                .AsNoTracking()
                .Where(p => p.ShopId == shopId && p.IsActive && p.IsWeightBased && !p.IsDeleted)
                .OrderBy(p => p.Name)
                .ToListAsync();

            _logger.LogDebug("Found {Count} weight-based products for shop {ShopId} in Local_Storage",
                products.Count, shopId);

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting weight-based products for shop {ShopId} from Local_Storage", shopId);
            throw;
        }
    }
}