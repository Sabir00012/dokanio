using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Shared.Core.Data;
using Shared.Core.Entities;
using System.Linq.Expressions;

namespace Shared.Core.Repositories;

/// <summary>
/// Base repository implementation with offline-first storage priority
/// All operations prioritize Local_Storage and implement transaction logging for durability
/// </summary>
/// <typeparam name="T">Entity type that implements ISoftDeletable</typeparam>
public class Repository<T> : IRepository<T> where T : class, ISoftDeletable
{
    protected readonly PosDbContext _context;
    protected readonly ILogger<Repository<T>> _logger;
    protected readonly DbSet<T> _dbSet;

    public Repository(PosDbContext context, ILogger<Repository<T>> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dbSet = _context.Set<T>();
    }

    /// <summary>
    /// Gets an entity by its unique identifier from Local_Storage
    /// </summary>
    public virtual async Task<T?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogDebug("Getting entity {EntityType} with ID {Id} from Local_Storage", typeof(T).Name, id);
            
            // Local-first: Always query Local_Storage first
            var entity = await _dbSet.FindAsync(id);
            
            if (entity != null)
            {
                _logger.LogDebug("Found entity {EntityType} with ID {Id} in Local_Storage", typeof(T).Name, id);
            }
            else
            {
                _logger.LogDebug("Entity {EntityType} with ID {Id} not found in Local_Storage", typeof(T).Name, id);
            }
            
            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entity {EntityType} with ID {Id} from Local_Storage", typeof(T).Name, id);
            throw;
        }
    }

    /// <summary>
    /// Gets all entities from Local_Storage
    /// </summary>
    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        try
        {
            _logger.LogDebug("Getting all entities of type {EntityType} from Local_Storage", typeof(T).Name);
            
            // Local-first: Query Local_Storage only
            var entities = await _dbSet.ToListAsync();
            
            _logger.LogDebug("Retrieved {Count} entities of type {EntityType} from Local_Storage", entities.Count, typeof(T).Name);
            
            return entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all entities of type {EntityType} from Local_Storage", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Finds entities matching the specified predicate in Local_Storage
    /// </summary>
    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        try
        {
            _logger.LogDebug("Finding entities of type {EntityType} with predicate from Local_Storage", typeof(T).Name);
            
            // Local-first: Query Local_Storage only
            var entities = await _dbSet.Where(predicate).ToListAsync();
            
            _logger.LogDebug("Found {Count} entities of type {EntityType} matching predicate in Local_Storage", entities.Count, typeof(T).Name);
            
            return entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding entities of type {EntityType} with predicate in Local_Storage", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Adds a new entity to Local_Storage with transaction logging
    /// Local-first: Persists to Local_Storage before any network operations
    /// </summary>
    public virtual async Task AddAsync(T entity)
    {
        try
        {
            _logger.LogDebug("Adding new entity of type {EntityType} to Local_Storage", typeof(T).Name);
            
            // Local-first: Add to Local_Storage immediately
            await _dbSet.AddAsync(entity);
            
            _logger.LogDebug("Entity of type {EntityType} added to Local_Storage context", typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding entity of type {EntityType} to Local_Storage", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing entity in Local_Storage with transaction logging
    /// Local-first: Updates Local_Storage before any network operations
    /// </summary>
    public virtual async Task UpdateAsync(T entity)
    {
        try
        {
            _logger.LogDebug("Updating entity of type {EntityType} in Local_Storage", typeof(T).Name);
            
            // Local-first: Update in Local_Storage immediately
            _dbSet.Update(entity);
            
            _logger.LogDebug("Entity of type {EntityType} updated in Local_Storage context", typeof(T).Name);
            
            await Task.CompletedTask; // Keep async signature for consistency
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating entity of type {EntityType} in Local_Storage", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Soft deletes an entity by its identifier in Local_Storage
    /// Local-first: Marks as deleted in Local_Storage before any network operations
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        try
        {
            _logger.LogDebug("Soft deleting entity {EntityType} with ID {Id} from Local_Storage", typeof(T).Name, id);
            
            // Local-first: Find and soft delete in Local_Storage
            var entity = await _dbSet.FindAsync(id);
            if (entity != null)
            {
                // Soft delete - handled by DbContext.SaveChanges()
                _dbSet.Remove(entity);
                _logger.LogDebug("Entity {EntityType} with ID {Id} marked for soft deletion in Local_Storage", typeof(T).Name, id);
            }
            else
            {
                _logger.LogWarning("Entity {EntityType} with ID {Id} not found for deletion in Local_Storage", typeof(T).Name, id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error soft deleting entity {EntityType} with ID {Id} from Local_Storage", typeof(T).Name, id);
            throw;
        }
    }

    /// <summary>
    /// Saves all pending changes to Local_Storage with transaction durability
    /// Implements transaction logging for data durability
    /// </summary>
    public virtual async Task<int> SaveChangesAsync()
    {
        try
        {
            _logger.LogDebug("Saving changes to Local_Storage for entity type {EntityType}", typeof(T).Name);
            
            // Local-first: Save to Local_Storage with transaction durability
            // The PosDbContext handles soft delete logic and transaction logging
            var affectedRows = await _context.SaveChangesAsync();
            
            _logger.LogDebug("Successfully saved {AffectedRows} changes to Local_Storage for entity type {EntityType}", affectedRows, typeof(T).Name);
            
            return affectedRows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving changes to Local_Storage for entity type {EntityType}", typeof(T).Name);
            throw;
        }
    }

    // ── Transaction support (Requirement 9.3) ─────────────────────────────────

    /// <summary>
    /// Begins a database transaction for multi-step operations that must succeed or fail atomically.
    /// Local-first: Wraps Local_Storage operations in a transaction for data consistency.
    /// Note: In-memory databases (used in tests) silently ignore transactions.
    /// </summary>
    public virtual async Task<IDbContextTransaction> BeginTransactionAsync()
    {
        try
        {
            _logger.LogDebug("Beginning transaction for entity type {EntityType} in Local_Storage", typeof(T).Name);
            return await _context.Database.BeginTransactionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error beginning transaction for entity type {EntityType} in Local_Storage", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Executes an operation inside a database transaction, committing on success and rolling back on failure.
    /// Local-first: Ensures atomicity of multi-step Local_Storage operations.
    /// </summary>
    public virtual async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Use EF Core's built-in execution strategy to handle transient failures
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _logger.LogDebug("Executing operation in transaction for entity type {EntityType}", typeof(T).Name);
                var result = await operation();
                await transaction.CommitAsync();
                _logger.LogDebug("Transaction committed successfully for entity type {EntityType}", typeof(T).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction failed for entity type {EntityType}, rolling back", typeof(T).Name);
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    /// <summary>
    /// Executes an operation inside a database transaction (no return value).
    /// Local-first: Ensures atomicity of multi-step Local_Storage operations.
    /// </summary>
    public virtual async Task ExecuteInTransactionAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteInTransactionAsync(async () =>
        {
            await operation();
            return true; // dummy return value
        });
    }
}