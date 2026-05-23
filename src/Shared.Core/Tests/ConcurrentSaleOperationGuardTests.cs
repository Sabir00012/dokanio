using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Core.Services;
using System.Diagnostics;
using Xunit;

namespace Shared.Core.Tests;

/// <summary>
/// Unit tests for <see cref="ConcurrentSaleOperationGuard"/>.
/// Validates mutual exclusion, timeout behaviour, and cleanup.
/// </summary>
public class ConcurrentSaleOperationGuardTests : IDisposable
{
    private readonly ConcurrentSaleOperationGuard _sut;

    public ConcurrentSaleOperationGuardTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        _sut = new ConcurrentSaleOperationGuard(sp.GetRequiredService<ILogger<ConcurrentSaleOperationGuard>>());
    }

    // ── Basic execution ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RunsOperation_ReturnsResult()
    {
        var saleId = Guid.NewGuid();
        var result = await _sut.ExecuteAsync(saleId, () => Task.FromResult(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteAsync_VoidOverload_RunsOperation()
    {
        var saleId = Guid.NewGuid();
        bool ran = false;
        await _sut.ExecuteAsync(saleId, () => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
    }

    // ── Mutual exclusion ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SameSaleId_SerializesOperations()
    {
        var saleId = Guid.NewGuid();
        var log = new List<int>();
        var tasks = new List<Task>();

        // Launch 5 concurrent operations on the same sale
        for (int i = 0; i < 5; i++)
        {
            var captured = i;
            tasks.Add(Task.Run(async () =>
            {
                await _sut.ExecuteAsync(saleId, async () =>
                {
                    log.Add(captured);
                    await Task.Delay(10); // simulate work
                    return true;
                });
            }));
        }

        await Task.WhenAll(tasks);

        // All 5 operations must have run
        Assert.Equal(5, log.Count);
    }

    [Fact]
    public async Task ExecuteAsync_DifferentSaleIds_RunConcurrently()
    {
        // Operations on different sales should not block each other
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, 5).Select(i =>
            _sut.ExecuteAsync(Guid.NewGuid(), async () =>
            {
                await Task.Delay(50); // 50 ms each
                return i;
            })).ToList();

        await Task.WhenAll(tasks);
        sw.Stop();

        // If they ran concurrently, total time should be much less than 5 * 50 ms = 250 ms
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Concurrent operations on different sales took {sw.ElapsedMilliseconds}ms, expected < 200ms");
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenLockHeld_ThrowsTimeoutException()
    {
        var saleId = Guid.NewGuid();
        var holdLock = new TaskCompletionSource<bool>();

        // Hold the lock indefinitely
        var holder = Task.Run(() => _sut.ExecuteAsync(saleId, async () =>
        {
            await holdLock.Task;
            return true;
        }));

        // Give the holder time to acquire the lock
        await Task.Delay(50);

        // Second operation should time out quickly
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _sut.ExecuteAsync(saleId, () => Task.FromResult(true), timeout: TimeSpan.FromMilliseconds(100)));

        // Release the holder
        holdLock.SetResult(true);
        await holder;
    }

    // ── Exception propagation ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenOperationThrows_PropagatesException()
    {
        var saleId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ExecuteAsync<bool>(saleId, () => throw new InvalidOperationException("test error")));
    }

    [Fact]
    public async Task ExecuteAsync_AfterException_LockIsReleased()
    {
        var saleId = Guid.NewGuid();

        // First call throws
        try { await _sut.ExecuteAsync<bool>(saleId, () => throw new Exception("boom")); }
        catch { /* expected */ }

        // Second call should succeed (lock was released)
        var result = await _sut.ExecuteAsync(saleId, () => Task.FromResult(99));
        Assert.Equal(99, result);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var guard = new ConcurrentSaleOperationGuard(
            new ServiceCollection().AddLogging().BuildServiceProvider()
                .GetRequiredService<ILogger<ConcurrentSaleOperationGuard>>());

        guard.Dispose();
        guard.Dispose(); // Should not throw
    }

    [Fact]
    public async Task ExecuteAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var guard = new ConcurrentSaleOperationGuard(
            new ServiceCollection().AddLogging().BuildServiceProvider()
                .GetRequiredService<ILogger<ConcurrentSaleOperationGuard>>());

        guard.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            guard.ExecuteAsync(Guid.NewGuid(), () => Task.FromResult(1)));
    }

    public void Dispose() => _sut.Dispose();
}
