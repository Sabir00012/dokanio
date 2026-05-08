using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Core.Data;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;

namespace Shared.Core.Repositories;

/// <summary>
/// Sale repository implementation with offline-first storage priority
/// </summary>
public class SaleRepository : Repository<Sale>, ISaleRepository
{
    public SaleRepository(PosDbContext context, ILogger<SaleRepository> logger) 
        : base(context, logger)
    {
    }

    /// <summary>
    /// Gets a sale by ID including customer relationship from Local_Storage
    /// </summary>
    public override async Task<Sale?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogDebug("Getting sale with ID {Id} including customer from Local_Storage", id);
            
            // Local-first: Query Local_Storage with customer relationship
            var sale = await _dbSet
                .Include(s => s.Customer)
                .Include(s => s.Items)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(s => s.Id == id);
            
            if (sale != null)
            {
                _logger.LogDebug("Found sale with ID {Id} in Local_Storage", id);
            }
            else
            {
                _logger.LogDebug("Sale with ID {Id} not found in Local_Storage", id);
            }
            
            return sale;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sale with ID {Id} from Local_Storage", id);
            throw;
        }
    }

    /// <summary>
    /// Gets all sales that haven't been synced to the server from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Sale>> GetUnsyncedAsync()
    {
        try
        {
            _logger.LogDebug("Getting unsynced sales from Local_Storage");
            
            // Local-first: Query Local_Storage only
            var unsyncedSales = await _dbSet
                .Include(s => s.Items)
                .Where(s => s.SyncStatus == SyncStatus.NotSynced || s.SyncStatus == SyncStatus.SyncFailed)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} unsynced sales in Local_Storage", unsyncedSales.Count);
            
            return unsyncedSales;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unsynced sales from Local_Storage");
            throw;
        }
    }

    /// <summary>
    /// Gets the total sales amount for a specific date from Local_Storage
    /// </summary>
    public async Task<decimal> GetDailySalesAsync(DateTime date)
    {
        try
        {
            _logger.LogDebug("Getting daily sales total for date {Date} from Local_Storage", date.Date);
            
            // Convert to UTC for consistent comparison with sale CreatedAt timestamps
            var startOfDay = date.Date.ToUniversalTime();
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);
            
            // Local-first: Query Local_Storage only
            var dailyTotal = await _dbSet
                .Where(s => s.CreatedAt >= startOfDay && s.CreatedAt <= endOfDay)
                .SumAsync(s => s.TotalAmount);
            
            _logger.LogDebug("Daily sales total for {Date}: {Total} from Local_Storage", date.Date, dailyTotal);
            
            return dailyTotal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily sales for date {Date} from Local_Storage", date.Date);
            throw;
        }
    }

    /// <summary>
    /// Gets all sales within a date range from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Sale>> GetSalesByDateRangeAsync(DateTime from, DateTime to)
    {
        try
        {
            _logger.LogDebug("Getting sales from {From} to {To} from Local_Storage", from, to);
            
            var startDate = from.Date;
            var endDate = to.Date.AddDays(1).AddTicks(-1);
            
            // Local-first: Query Local_Storage only
            var salesInRange = await _dbSet
                .Include(s => s.Items)
                .Where(s => s.CreatedAt >= startDate && s.CreatedAt <= endDate)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} sales from {From} to {To} in Local_Storage", salesInRange.Count, from, to);
            
            return salesInRange;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sales from {From} to {To} from Local_Storage", from, to);
            throw;
        }
    }

    /// <summary>
    /// Gets sales for a specific device from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Sale>> GetSalesByDeviceAsync(Guid deviceId)
    {
        try
        {
            _logger.LogDebug("Getting sales for device {DeviceId} from Local_Storage", deviceId);
            
            // Local-first: Query Local_Storage only
            var deviceSales = await _dbSet
                .Include(s => s.Items)
                .Where(s => s.DeviceId == deviceId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} sales for device {DeviceId} in Local_Storage", deviceSales.Count, deviceId);
            
            return deviceSales;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sales for device {DeviceId} from Local_Storage", deviceId);
            throw;
        }
    }

    /// <summary>
    /// Gets the most recent sale from Local_Storage
    /// </summary>
    public async Task<Sale?> GetLatestSaleAsync()
    {
        try
        {
            _logger.LogDebug("Getting latest sale from Local_Storage");
            
            // Local-first: Query Local_Storage only
            var latestSale = await _dbSet
                .Include(s => s.Items)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
            
            if (latestSale != null)
            {
                _logger.LogDebug("Found latest sale {InvoiceNumber} from {CreatedAt} in Local_Storage", latestSale.InvoiceNumber, latestSale.CreatedAt);
            }
            else
            {
                _logger.LogDebug("No sales found in Local_Storage");
            }
            
            return latestSale;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest sale from Local_Storage");
            throw;
        }
    }

    /// <summary>
    /// Gets sales by invoice number from Local_Storage
    /// </summary>
    public async Task<Sale?> GetByInvoiceNumberAsync(string invoiceNumber)
    {
        try
        {
            _logger.LogDebug("Getting sale by invoice number {InvoiceNumber} from Local_Storage", invoiceNumber);
            
            if (string.IsNullOrWhiteSpace(invoiceNumber))
            {
                _logger.LogWarning("Invoice number is null or empty");
                return null;
            }
            
            // Local-first: Query Local_Storage only
            var sale = await _dbSet
                .Include(s => s.Items)
                .FirstOrDefaultAsync(s => s.InvoiceNumber == invoiceNumber);
            
            if (sale != null)
            {
                _logger.LogDebug("Found sale with invoice number {InvoiceNumber} in Local_Storage", invoiceNumber);
            }
            else
            {
                _logger.LogDebug("Sale with invoice number {InvoiceNumber} not found in Local_Storage", invoiceNumber);
            }
            
            return sale;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sale by invoice number {InvoiceNumber} from Local_Storage", invoiceNumber);
            throw;
        }
    }

    /// <summary>
    /// Gets all sales within a date range for a specific shop from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Sale>> GetSalesByShopAndDateRangeAsync(Guid shopId, DateTime from, DateTime to)
    {
        try
        {
            _logger.LogDebug("Getting sales for shop {ShopId} from {From} to {To} from Local_Storage", shopId, from, to);
            
            var startDate = from.Date;
            var endDate = to.Date.AddDays(1).AddTicks(-1);
            
            // Local-first: Query Local_Storage only
            var salesInRange = await _dbSet
                .Include(s => s.Items)
                .Where(s => s.ShopId == shopId && s.CreatedAt >= startDate && s.CreatedAt <= endDate)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} sales for shop {ShopId} from {From} to {To} in Local_Storage", 
                salesInRange.Count, shopId, from, to);
            
            return salesInRange;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sales for shop {ShopId} from {From} to {To} from Local_Storage", 
                shopId, from, to);
            throw;
        }
    }

    /// <summary>
    /// Gets all sales for a specific shop from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Sale>> GetSalesByShopAsync(Guid shopId)
    {
        try
        {
            _logger.LogDebug("Getting all sales for shop {ShopId} from Local_Storage", shopId);
            
            // Local-first: Query Local_Storage only
            var shopSales = await _dbSet
                .Include(s => s.Items)
                .Where(s => s.ShopId == shopId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            
            _logger.LogDebug("Found {Count} sales for shop {ShopId} in Local_Storage", shopSales.Count, shopId);
            
            return shopSales;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sales for shop {ShopId} from Local_Storage", shopId);
            throw;
        }
    }

    /// <summary>
    /// Returns a paginated page of sale history with optional filters.
    /// Uses AsNoTracking for read-only performance (Requirement 9.6).
    /// </summary>
    public async Task<PagedResult<Sale>> GetSaleHistoryPagedAsync(
        DateTime from,
        DateTime to,
        Guid? shopId = null,
        SaleStatus? status = null,
        int page = 0,
        int pageSize = 20)
    {
        if (page < 0) page = 0;
        pageSize = Math.Clamp(pageSize, 1, 200);

        try
        {
            _logger.LogDebug(
                "Getting paged sale history: from={From}, to={To}, shopId={ShopId}, status={Status}, page={Page}, pageSize={PageSize}",
                from, to, shopId, status, page, pageSize);

            var startDate = from.Date;
            var endDate   = to.Date.AddDays(1).AddTicks(-1);

            // Build the base query with AsNoTracking for read-only performance
            var query = _dbSet
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startDate && s.CreatedAt <= endDate);

            if (shopId.HasValue)
                query = query.Where(s => s.ShopId == shopId.Value);

            if (status.HasValue)
                query = query.Where(s => s.Status == status.Value);

            // Get total count before pagination
            var totalCount = await query.CountAsync();

            // Fetch the page with Include for navigation properties
            var items = await query
                .OrderByDescending(s => s.CreatedAt)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(s => s.Items)
                .ToListAsync();

            _logger.LogDebug(
                "Paged sale history: returned {Count}/{Total} items (page {Page})",
                items.Count, totalCount, page);

            return PagedResult<Sale>.Create(items, totalCount, page, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged sale history from Local_Storage");
            throw;
        }
    }

    // ── Enhanced query methods (Requirement 9.3) ──────────────────────────────

    /// <summary>
    /// Gets all active (Draft or Active status) sales, optionally filtered by shop.
    /// Uses AsNoTracking for read-only performance (Requirement 9.3).
    /// </summary>
    public async Task<IEnumerable<Sale>> GetActiveSalesAsync(Guid? shopId = null)
    {
        try
        {
            _logger.LogDebug("Getting active sales from Local_Storage (shopId={ShopId})", shopId);

            var query = _dbSet
                .AsNoTracking()
                .Where(s => s.Status == SaleStatus.Draft || s.Status == SaleStatus.Active);

            if (shopId.HasValue)
                query = query.Where(s => s.ShopId == shopId.Value);

            var activeSales = await query
                .OrderByDescending(s => s.UpdatedAt)
                .Include(s => s.Items)
                .ToListAsync();

            _logger.LogDebug("Found {Count} active sales in Local_Storage", activeSales.Count);
            return activeSales;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active sales from Local_Storage");
            throw;
        }
    }

    /// <summary>
    /// Gets all sales for a specific customer, ordered by most recent first.
    /// Uses AsNoTracking for read-only performance (Requirement 9.3).
    /// </summary>
    public async Task<IEnumerable<Sale>> GetSalesByCustomerAsync(Guid customerId, int limit = 50)
    {
        if (limit <= 0) limit = 50;
        limit = Math.Min(limit, 500); // cap to prevent runaway queries

        try
        {
            _logger.LogDebug("Getting sales for customer {CustomerId} (limit={Limit}) from Local_Storage", customerId, limit);

            var sales = await _dbSet
                .AsNoTracking()
                .Where(s => s.CustomerId == customerId && s.Status == SaleStatus.Completed)
                .OrderByDescending(s => s.CreatedAt)
                .Take(limit)
                .Include(s => s.Items)
                .ToListAsync();

            _logger.LogDebug("Found {Count} sales for customer {CustomerId} in Local_Storage", sales.Count, customerId);
            return sales;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sales for customer {CustomerId} from Local_Storage", customerId);
            throw;
        }
    }

    /// <summary>
    /// Gets the total sales amount for a specific date and optional shop from Local_Storage.
    /// Only counts completed sales (Requirement 9.3).
    /// </summary>
    public async Task<decimal> GetDailySalesAmountAsync(DateTime date, Guid? shopId = null)
    {
        try
        {
            _logger.LogDebug("Getting daily sales amount for date {Date} (shopId={ShopId}) from Local_Storage", date.Date, shopId);

            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            var query = _dbSet
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startOfDay && s.CreatedAt <= endOfDay
                         && s.Status == SaleStatus.Completed);

            if (shopId.HasValue)
                query = query.Where(s => s.ShopId == shopId.Value);

            var total = await query.SumAsync(s => s.FinalTotal > 0 ? s.FinalTotal : s.TotalAmount);

            _logger.LogDebug("Daily sales amount for {Date}: {Total} from Local_Storage", date.Date, total);
            return total;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily sales amount for date {Date} from Local_Storage", date.Date);
            throw;
        }
    }

    /// <summary>
    /// Gets the count of completed sales for a specific date and optional shop from Local_Storage.
    /// </summary>
    public async Task<int> GetDailySalesCountAsync(DateTime date, Guid? shopId = null)
    {
        try
        {
            _logger.LogDebug("Getting daily sales count for date {Date} (shopId={ShopId}) from Local_Storage", date.Date, shopId);

            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            var query = _dbSet
                .AsNoTracking()
                .Where(s => s.CreatedAt >= startOfDay && s.CreatedAt <= endOfDay
                         && s.Status == SaleStatus.Completed);

            if (shopId.HasValue)
                query = query.Where(s => s.ShopId == shopId.Value);

            var count = await query.CountAsync();

            _logger.LogDebug("Daily sales count for {Date}: {Count} from Local_Storage", date.Date, count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily sales count for date {Date} from Local_Storage", date.Date);
            throw;
        }
    }
}