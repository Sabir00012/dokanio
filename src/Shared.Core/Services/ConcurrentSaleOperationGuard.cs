using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Shared.Core.Services;

/// <summary>
/// Prevents race conditions when multiple concurrent requests operate on the same sale.
/// Uses a per-sale-ID <see cref="SemaphoreSlim"/> so unrelated sales are never blocked.
/// </summary>
/// <remarks>
/// Register as <c>Singleton</c> so the lock dictionary is shared across all scoped services
/// within the same process.
/// </remarks>
public class ConcurrentSaleOperationGuard : IDisposable
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ILogger<ConcurrentSaleOperationGuard> _logger;
    private bool _disposed;

    public ConcurrentSaleOperationGuard(ILogger<ConcurrentSaleOperationGuard> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Acquires the per-sale lock and executes <paramref name="operation"/>.
    /// Throws <see cref="TimeoutException"/> if the lock cannot be acquired within
    /// <paramref name="timeout"/> (default 5 s).
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Guid saleId,
        Func<Task<T>> operation,
        TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var semaphore = _locks.GetOrAdd(saleId, _ => new SemaphoreSlim(1, 1));
        var waitTime  = timeout ?? TimeSpan.FromSeconds(5);

        _logger.LogDebug("Acquiring lock for saleId={SaleId}", saleId);

        bool acquired = await semaphore.WaitAsync(waitTime);
        if (!acquired)
        {
            _logger.LogWarning(
                "Timeout acquiring lock for saleId={SaleId} after {Timeout}",
                saleId, waitTime);
            throw new TimeoutException(
                $"Could not acquire operation lock for sale {saleId} within {waitTime}.");
        }

        try
        {
            _logger.LogDebug("Lock acquired for saleId={SaleId}", saleId);
            return await operation();
        }
        finally
        {
            semaphore.Release();
            _logger.LogDebug("Lock released for saleId={SaleId}", saleId);

            // Clean up the semaphore if no other thread is waiting on it.
            // This prevents unbounded growth of the dictionary for long-running processes.
            if (semaphore.CurrentCount == 1)
            {
                _locks.TryRemove(saleId, out _);
            }
        }
    }

    /// <summary>
    /// Overload for void-returning operations.
    /// </summary>
    public async Task ExecuteAsync(
        Guid saleId,
        Func<Task> operation,
        TimeSpan? timeout = null)
    {
        await ExecuteAsync<bool>(saleId, async () =>
        {
            await operation();
            return true;
        }, timeout);
    }

    /// <summary>Returns the number of active per-sale locks (useful for diagnostics).</summary>
    public int ActiveLockCount => _locks.Count;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }
        _locks.Clear();
    }
}
