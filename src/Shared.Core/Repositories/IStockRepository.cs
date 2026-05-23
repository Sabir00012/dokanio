using Shared.Core.Entities;

namespace Shared.Core.Repositories;

/// <summary>
/// Specialized repository interface for Stock entities
/// </summary>
public interface IStockRepository : IRepository<Stock>
{
    /// <summary>
    /// Gets stock information for a specific product
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>Stock information for the product, null if not found</returns>
    Task<Stock?> GetByProductIdAsync(Guid productId);
    
    /// <summary>
    /// Gets all products with low stock (below specified threshold)
    /// </summary>
    /// <param name="threshold">Stock quantity threshold</param>
    /// <returns>Collection of stock entries below the threshold</returns>
    Task<IEnumerable<Stock>> GetLowStockAsync(int threshold = 10);
    
    /// <summary>
    /// Gets all stock entries that need to be synced to the server
    /// </summary>
    /// <returns>Collection of unsynced stock entries</returns>
    Task<IEnumerable<Stock>> GetUnsyncedAsync();
    
    /// <summary>
    /// Updates stock quantity for a specific product
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="quantityChange">Quantity change (positive for increase, negative for decrease)</param>
    /// <param name="deviceId">Device making the change</param>
    Task UpdateStockQuantityAsync(Guid productId, int quantityChange, Guid deviceId);
    
    /// <summary>
    /// Gets current stock quantity for a product
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>Current stock quantity, 0 if product not found</returns>
    Task<int> GetStockQuantityAsync(Guid productId);
    
    /// <summary>
    /// Gets all stock entries for products in a specific category
    /// </summary>
    /// <param name="category">Product category</param>
    /// <returns>Collection of stock entries for products in the category</returns>
    Task<IEnumerable<Stock>> GetStockByCategoryAsync(string category);
    
    /// <summary>
    /// Checks if a product has sufficient stock for a sale
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="requiredQuantity">Required quantity</param>
    /// <returns>True if sufficient stock is available, false otherwise</returns>
    Task<bool> HasSufficientStockAsync(Guid productId, int requiredQuantity);
    
    /// <summary>
    /// Gets all stock entries for a specific shop
    /// </summary>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>Collection of stock entries for the shop</returns>
    Task<IEnumerable<Stock>> GetStockByShopAsync(Guid shopId);

    // ── Enhanced query methods (Requirement 9.3) ──────────────────────────────

    /// <summary>
    /// Gets stock levels for multiple products in a single batch query.
    /// More efficient than calling <see cref="GetByProductIdAsync"/> in a loop.
    /// </summary>
    /// <param name="productIds">Collection of product identifiers.</param>
    /// <returns>Dictionary mapping product ID to stock quantity (0 if no stock record exists).</returns>
    Task<Dictionary<Guid, int>> GetStockQuantitiesBatchAsync(IEnumerable<Guid> productIds);

    /// <summary>
    /// Atomically decrements stock for multiple products in a single transaction.
    /// Used during sale completion to reduce inventory for all sold items at once.
    /// Throws <see cref="InvalidOperationException"/> if any product has insufficient stock.
    /// </summary>
    /// <param name="reductions">Dictionary mapping product ID to quantity to reduce.</param>
    /// <param name="deviceId">Device making the change (for sync tracking).</param>
    Task DeductStockBatchAsync(Dictionary<Guid, int> reductions, Guid deviceId);
}