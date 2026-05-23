using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Core.Data;
using Shared.Core.Entities;
using Shared.Core.Enums;

namespace Shared.Core.Repositories;

/// <summary>
/// Supplier repository implementation with offline-first storage priority
/// </summary>
public class SupplierRepository : Repository<Supplier>, ISupplierRepository
{
    public SupplierRepository(PosDbContext context, ILogger<SupplierRepository> logger)
        : base(context, logger)
    {
    }

    /// <summary>
    /// Gets all active suppliers from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Supplier>> GetActiveSuppliersAsync()
    {
        try
        {
            _logger.LogDebug("Getting all active suppliers from Local_Storage");

            var suppliers = await _dbSet
                .Where(s => s.IsActive && !s.IsDeleted)
                .OrderBy(s => s.Name)
                .ToListAsync();

            _logger.LogDebug("Found {Count} active suppliers in Local_Storage", suppliers.Count);

            return suppliers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active suppliers from Local_Storage");
            throw;
        }
    }

    /// <summary>
    /// Searches suppliers by name, contact person, or phone from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Supplier>> SearchAsync(string searchTerm)
    {
        try
        {
            _logger.LogDebug("Searching suppliers by term {SearchTerm} from Local_Storage", searchTerm);

            if (string.IsNullOrWhiteSpace(searchTerm))
                return Enumerable.Empty<Supplier>();

            var lower = searchTerm.ToLower();

            var results = await _dbSet
                .Where(s => s.IsActive && !s.IsDeleted &&
                            (s.Name.ToLower().Contains(lower) ||
                             s.ContactPerson.ToLower().Contains(lower) ||
                             s.Phone.Contains(searchTerm)))
                .OrderBy(s => s.Name)
                .ToListAsync();

            _logger.LogDebug("Found {Count} suppliers matching '{SearchTerm}' in Local_Storage", results.Count, searchTerm);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching suppliers by term {SearchTerm} from Local_Storage", searchTerm);
            throw;
        }
    }

    /// <summary>
    /// Gets suppliers that need to be synced to the server from Local_Storage
    /// </summary>
    public async Task<IEnumerable<Supplier>> GetUnsyncedAsync()
    {
        try
        {
            _logger.LogDebug("Getting unsynced suppliers from Local_Storage");

            var unsynced = await _dbSet
                .Where(s => s.SyncStatus == SyncStatus.NotSynced || s.SyncStatus == SyncStatus.SyncFailed)
                .ToListAsync();

            _logger.LogDebug("Found {Count} unsynced suppliers in Local_Storage", unsynced.Count);

            return unsynced;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unsynced suppliers from Local_Storage");
            throw;
        }
    }
}
