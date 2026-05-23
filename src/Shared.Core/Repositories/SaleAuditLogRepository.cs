using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Core.Data;
using Shared.Core.Entities;

namespace Shared.Core.Repositories;

/// <summary>
/// EF Core repository for <see cref="SaleAuditLog"/> records.
/// Provides efficient date-range and user-based queries with appropriate indexes.
/// </summary>
public class SaleAuditLogRepository : ISaleAuditLogRepository
{
    private readonly PosDbContext _context;
    private readonly ILogger<SaleAuditLogRepository> _logger;

    public SaleAuditLogRepository(PosDbContext context, ILogger<SaleAuditLogRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task AddAsync(SaleAuditLog entry)
    {
        try
        {
            await _context.SaleAuditLogs.AddAsync(entry);
            _logger.LogDebug("Queued SaleAuditLog {Id} (type: {EventType}) for sale {SaleId}",
                entry.Id, entry.EventType, entry.SaleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queuing SaleAuditLog for sale {SaleId}", entry.SaleId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync()
    {
        try
        {
            return await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving SaleAuditLog changes");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SaleAuditLog>> GetBySaleIdAsync(Guid saleId)
    {
        try
        {
            return await _context.SaleAuditLogs
                .Where(l => l.SaleId == saleId && !l.IsDeleted)
                .OrderBy(l => l.Timestamp)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit logs for sale {SaleId}", saleId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SaleAuditLog>> GetByUserIdAsync(Guid userId, DateTime fromDate, DateTime toDate)
    {
        try
        {
            return await _context.SaleAuditLogs
                .Where(l => l.UserId == userId
                         && l.Timestamp >= fromDate
                         && l.Timestamp <= toDate
                         && !l.IsDeleted)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit logs for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SaleAuditLog>> GetByDateRangeAsync(
        DateTime fromDate, DateTime toDate, Guid? shopId = null)
    {
        try
        {
            IQueryable<SaleAuditLog> query = _context.SaleAuditLogs
                .Where(l => l.Timestamp >= fromDate
                         && l.Timestamp <= toDate
                         && !l.IsDeleted);

            if (shopId.HasValue)
            {
                // Join against Sales to filter by shop
                var saleIds = _context.Sales
                    .Where(s => s.ShopId == shopId.Value && !s.IsDeleted)
                    .Select(s => s.Id);

                query = query.Where(l => saleIds.Contains(l.SaleId));
            }

            return await query
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit logs for date range {From} – {To}", fromDate, toDate);
            throw;
        }
    }
}
