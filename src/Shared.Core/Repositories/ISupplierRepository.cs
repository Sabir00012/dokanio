using Shared.Core.Entities;

namespace Shared.Core.Repositories;

/// <summary>
/// Specialized repository interface for Supplier entities
/// </summary>
public interface ISupplierRepository : IRepository<Supplier>
{
    /// <summary>
    /// Gets all active suppliers from Local_Storage
    /// </summary>
    Task<IEnumerable<Supplier>> GetActiveSuppliersAsync();

    /// <summary>
    /// Searches suppliers by name, contact person, or phone from Local_Storage
    /// </summary>
    Task<IEnumerable<Supplier>> SearchAsync(string searchTerm);

    /// <summary>
    /// Gets suppliers that need to be synced to the server
    /// </summary>
    Task<IEnumerable<Supplier>> GetUnsyncedAsync();
}
