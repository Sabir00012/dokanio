using Shared.Core.Entities;

namespace Shared.Core.Repositories;

/// <summary>
/// Repository interface for querying and persisting <see cref="SaleAuditLog"/> records.
/// </summary>
public interface ISaleAuditLogRepository
{
    /// <summary>Persists a new audit log entry.</summary>
    Task AddAsync(SaleAuditLog entry);

    /// <summary>Saves pending changes to the underlying store.</summary>
    Task<int> SaveChangesAsync();

    /// <summary>Returns all audit logs for a specific sale, ordered by timestamp ascending.</summary>
    Task<IEnumerable<SaleAuditLog>> GetBySaleIdAsync(Guid saleId);

    /// <summary>
    /// Returns audit logs created by a specific user within the given date range,
    /// ordered by timestamp descending.
    /// </summary>
    Task<IEnumerable<SaleAuditLog>> GetByUserIdAsync(Guid userId, DateTime fromDate, DateTime toDate);

    /// <summary>
    /// Returns all audit logs within the given date range, optionally filtered by shop.
    /// The shop filter is applied by joining against the Sale table.
    /// </summary>
    Task<IEnumerable<SaleAuditLog>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, Guid? shopId = null);
}
