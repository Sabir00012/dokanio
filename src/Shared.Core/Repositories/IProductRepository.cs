using Shared.Core.DTOs;
using Shared.Core.Entities;

namespace Shared.Core.Repositories;

/// <summary>
/// Specialized repository interface for Product entities
/// </summary>
public interface IProductRepository : IRepository<Product>
{
    /// <summary>
    /// Gets a product by its barcode
    /// </summary>
    /// <param name="barcode">Product barcode</param>
    /// <returns>Product if found, null otherwise</returns>
    Task<Product?> GetByBarcodeAsync(string barcode);
    
    /// <summary>
    /// Gets all active products in a specific category
    /// </summary>
    /// <param name="category">Product category</param>
    /// <returns>Collection of active products in the category</returns>
    Task<IEnumerable<Product>> GetActiveByCategoryAsync(string category);
    
    /// <summary>
    /// Gets all medicine products expiring before the specified date
    /// </summary>
    /// <param name="beforeDate">Expiry date threshold</param>
    /// <returns>Collection of expiring medicine products</returns>
    Task<IEnumerable<Product>> GetExpiringMedicinesAsync(DateTime beforeDate);
    
    /// <summary>
    /// Gets all active products
    /// </summary>
    /// <returns>Collection of active products</returns>
    Task<IEnumerable<Product>> GetActiveProductsAsync();
    
    /// <summary>
    /// Gets products that need to be synced to the server
    /// </summary>
    /// <returns>Collection of unsynced products</returns>
    Task<IEnumerable<Product>> GetUnsyncedAsync();
    
    /// <summary>
    /// Searches products by name or barcode
    /// </summary>
    /// <param name="searchTerm">Search term</param>
    /// <returns>Collection of matching products</returns>
    Task<IEnumerable<Product>> SearchAsync(string searchTerm);
    
    /// <summary>
    /// Gets all products for a specific shop
    /// </summary>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>Collection of products for the shop</returns>
    Task<IEnumerable<Product>> GetProductsByShopAsync(Guid shopId);

    // ── Paginated queries (Requirement 9.6) ───────────────────────────────────

    /// <summary>
    /// Returns a paginated page of products for a shop, optionally filtered by search term.
    /// Uses <c>AsNoTracking</c> for read-only performance.
    /// </summary>
    /// <param name="shopId">Shop identifier.</param>
    /// <param name="searchTerm">Optional name/barcode search term.</param>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Items per page (1–200).</param>
    Task<PagedResult<Product>> SearchProductsPagedAsync(
        Guid shopId,
        string? searchTerm = null,
        int page = 0,
        int pageSize = 20);

    // ── Enhanced query methods (Requirement 9.3, 9.4) ─────────────────────────

    /// <summary>
    /// Gets a product by ID with its current stock level in a single query.
    /// Avoids a separate stock lookup for the common "add to sale" hot path.
    /// </summary>
    /// <param name="productId">Product identifier.</param>
    /// <returns>Product with StockEntries populated, or null if not found.</returns>
    Task<Product?> GetProductWithStockAsync(Guid productId);

    /// <summary>
    /// Gets multiple products by their IDs in a single batch query.
    /// More efficient than calling <see cref="IRepository{T}.GetByIdAsync"/> in a loop.
    /// </summary>
    /// <param name="productIds">Collection of product identifiers.</param>
    /// <returns>Dictionary mapping product ID to product (only found products are included).</returns>
    Task<Dictionary<Guid, Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds);

    /// <summary>
    /// Gets all active products for a shop that are weight-based.
    /// </summary>
    /// <param name="shopId">Shop identifier.</param>
    /// <returns>Collection of active weight-based products.</returns>
    Task<IEnumerable<Product>> GetWeightBasedProductsAsync(Guid shopId);
}