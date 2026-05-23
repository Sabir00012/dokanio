using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;

namespace Shared.Core.Repositories;

/// <summary>
/// Specialized repository interface for Sale entities
/// </summary>
public interface ISaleRepository : IRepository<Sale>
{
    /// <summary>
    /// Gets all sales that haven't been synced to the server
    /// </summary>
    /// <returns>Collection of unsynced sales</returns>
    Task<IEnumerable<Sale>> GetUnsyncedAsync();
    
    /// <summary>
    /// Gets the total sales amount for a specific date
    /// </summary>
    /// <param name="date">Date to calculate sales for</param>
    /// <returns>Total sales amount for the date</returns>
    Task<decimal> GetDailySalesAsync(DateTime date);
    
    /// <summary>
    /// Gets all sales within a date range
    /// </summary>
    /// <param name="from">Start date (inclusive)</param>
    /// <param name="to">End date (inclusive)</param>
    /// <returns>Collection of sales within the date range</returns>
    Task<IEnumerable<Sale>> GetSalesByDateRangeAsync(DateTime from, DateTime to);
    
    /// <summary>
    /// Gets sales for a specific device
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <returns>Collection of sales for the device</returns>
    Task<IEnumerable<Sale>> GetSalesByDeviceAsync(Guid deviceId);
    
    /// <summary>
    /// Gets the most recent sale
    /// </summary>
    /// <returns>Most recent sale if any exists, null otherwise</returns>
    Task<Sale?> GetLatestSaleAsync();
    
    /// <summary>
    /// Gets sales by invoice number
    /// </summary>
    /// <param name="invoiceNumber">Invoice number</param>
    /// <returns>Sale with the specified invoice number, null if not found</returns>
    Task<Sale?> GetByInvoiceNumberAsync(string invoiceNumber);
    
    /// <summary>
    /// Gets all sales within a date range for a specific shop
    /// </summary>
    /// <param name="shopId">Shop identifier</param>
    /// <param name="from">Start date (inclusive)</param>
    /// <param name="to">End date (inclusive)</param>
    /// <returns>Collection of sales within the date range for the shop</returns>
    Task<IEnumerable<Sale>> GetSalesByShopAndDateRangeAsync(Guid shopId, DateTime from, DateTime to);
    
    /// <summary>
    /// Gets all sales for a specific shop
    /// </summary>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>Collection of sales for the shop</returns>
    Task<IEnumerable<Sale>> GetSalesByShopAsync(Guid shopId);

    // ── Paginated queries (Requirement 9.6) ───────────────────────────────────

    /// <summary>
    /// Returns a paginated page of sale history filtered by date range, shop, and/or status.
    /// Uses <c>AsNoTracking</c> and a <c>Select</c> projection for read-only performance.
    /// </summary>
    /// <param name="from">Inclusive start date (UTC).</param>
    /// <param name="to">Inclusive end date (UTC).</param>
    /// <param name="shopId">Optional shop filter.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Items per page (1–200).</param>
    Task<PagedResult<Sale>> GetSaleHistoryPagedAsync(
        DateTime from,
        DateTime to,
        Guid? shopId = null,
        SaleStatus? status = null,
        int page = 0,
        int pageSize = 20);

    // ── Enhanced query methods (Requirement 9.3) ──────────────────────────────

    /// <summary>
    /// Gets all active (Draft or Active status) sales, optionally filtered by shop.
    /// Uses <c>AsNoTracking</c> for read-only performance.
    /// </summary>
    /// <param name="shopId">Optional shop filter.</param>
    /// <returns>Collection of active sales.</returns>
    Task<IEnumerable<Sale>> GetActiveSalesAsync(Guid? shopId = null);

    /// <summary>
    /// Gets all sales for a specific customer, ordered by most recent first.
    /// </summary>
    /// <param name="customerId">Customer identifier.</param>
    /// <param name="limit">Maximum number of sales to return (default 50).</param>
    /// <returns>Collection of sales for the customer.</returns>
    Task<IEnumerable<Sale>> GetSalesByCustomerAsync(Guid customerId, int limit = 50);

    /// <summary>
    /// Gets the total sales amount for a specific date and optional shop.
    /// </summary>
    /// <param name="date">Date to calculate sales for.</param>
    /// <param name="shopId">Optional shop filter.</param>
    /// <returns>Total sales amount for the date.</returns>
    Task<decimal> GetDailySalesAmountAsync(DateTime date, Guid? shopId = null);

    /// <summary>
    /// Gets the count of completed sales for a specific date and optional shop.
    /// </summary>
    /// <param name="date">Date to count sales for.</param>
    /// <param name="shopId">Optional shop filter.</param>
    /// <returns>Number of completed sales for the date.</returns>
    Task<int> GetDailySalesCountAsync(DateTime date, Guid? shopId = null);
}