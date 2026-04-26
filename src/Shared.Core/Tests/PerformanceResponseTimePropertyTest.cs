using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Core.Entities;
using Shared.Core.Services;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Shared.Core.Tests;

/// <summary>
/// Property-based tests for performance response time.
///
/// Feature: sales-service-implementation, Property 17: Performance Response Time
/// **Validates: Requirements 9.1, 9.5**
///
/// Property: For any sale operation under normal load, the system should respond within
/// 200ms and handle concurrent operations without performance degradation.
/// </summary>
public class PerformanceResponseTimePropertyTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ISalesCacheService _cacheService;
    private readonly ConcurrentSaleOperationGuard _guard;
    private readonly ServiceProvider _sp;

    // Requirement 9.1: 200ms threshold for sale operations
    private const int SaleOperationThresholdMs = 200;

    // Requirement 9.2: 100ms threshold for calculations
    private const int CalculationThresholdMs = 100;

    // Minimum iterations per the spec
    private const int MinIterations = 100;

    public PerformanceResponseTimePropertyTest(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        _sp = services.BuildServiceProvider();

        var logger1 = _sp.GetRequiredService<ILogger<SalesCacheService>>();
        var logger2 = _sp.GetRequiredService<ILogger<ConcurrentSaleOperationGuard>>();
        _cacheService = new SalesCacheService(_sp.GetRequiredService<IMemoryCache>(), logger1);
        _guard = new ConcurrentSaleOperationGuard(logger2);
    }

    /// <summary>
    /// **Validates: Requirements 9.1, 9.5**
    ///
    /// Property 17: Performance Response Time
    /// For any product cache read/write operation, the operation must complete within
    /// the 200ms threshold defined for sale operations.
    /// </summary>
    [Fact]
    public async Task Property17_CacheOperations_AlwaysCompleteWithin200ms()
    {
        var random = new Random(42);
        int passCount = 0;
        var violations = new List<(string Op, long Ms)>();

        for (int i = 0; i < MinIterations; i++)
        {
            var product = GenerateRandomProduct(random);

            // Measure SET
            var sw = Stopwatch.StartNew();
            await _cacheService.SetProductAsync(product);
            sw.Stop();
            if (sw.ElapsedMilliseconds > SaleOperationThresholdMs)
                violations.Add(("SetProduct", sw.ElapsedMilliseconds));

            // Measure GET (cache hit)
            sw.Restart();
            var cached = await _cacheService.GetProductByIdAsync(product.Id);
            sw.Stop();
            if (sw.ElapsedMilliseconds > SaleOperationThresholdMs)
                violations.Add(("GetProduct", sw.ElapsedMilliseconds));

            Assert.NotNull(cached);
            passCount++;
        }

        _output.WriteLine($"Passed {passCount}/{MinIterations} iterations");
        if (violations.Count > 0)
        {
            _output.WriteLine($"Violations: {string.Join(", ", violations.Select(v => $"{v.Op}={v.Ms}ms"))}");
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// **Validates: Requirements 9.1, 9.5**
    ///
    /// Property 17: Performance Response Time (concurrent operations)
    /// For any set of concurrent sale operations on different sales, each individual
    /// operation must complete within 200ms (no degradation from concurrency).
    /// </summary>
    [Fact]
    public async Task Property17_ConcurrentGuardOperations_NoPerformanceDegradation()
    {
        var random = new Random(42);
        var violations = new List<(int Iteration, long Ms)>();

        // Run MinIterations / 10 rounds of 10 concurrent operations each
        int rounds = MinIterations / 10;
        int passCount = 0;

        for (int round = 0; round < rounds; round++)
        {
            // 10 concurrent operations on 10 different sales
            var tasks = Enumerable.Range(0, 10).Select(async j =>
            {
                var saleId = Guid.NewGuid(); // different sale per task → no contention
                var sw = Stopwatch.StartNew();
                await _guard.ExecuteAsync(saleId, async () =>
                {
                    // Simulate a lightweight sale operation (cache lookup)
                    var product = GenerateRandomProduct(random);
                    await _cacheService.SetProductAsync(product);
                    await _cacheService.GetProductByIdAsync(product.Id);
                    return true;
                });
                sw.Stop();
                return sw.ElapsedMilliseconds;
            }).ToList();

            var durations = await Task.WhenAll(tasks);

            foreach (var (duration, idx) in durations.Select((d, i) => (d, i)))
            {
                if (duration > SaleOperationThresholdMs)
                    violations.Add((round * 10 + idx, duration));
                else
                    passCount++;
            }
        }

        _output.WriteLine($"Passed {passCount}/{MinIterations} concurrent operations within {SaleOperationThresholdMs}ms");
        if (violations.Count > 0)
        {
            _output.WriteLine($"Violations: {string.Join(", ", violations.Select(v => $"iter={v.Iteration} {v.Ms}ms"))}");
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// **Validates: Requirements 9.1, 9.5**
    ///
    /// Property 17: Performance Response Time (tax rate cache)
    /// For any shop tax rate cache operation, the operation must complete within 200ms.
    /// </summary>
    [Fact]
    public async Task Property17_TaxRateCacheOperations_AlwaysCompleteWithin200ms()
    {
        var random = new Random(42);
        var violations = new List<(string Op, long Ms)>();
        int passCount = 0;

        for (int i = 0; i < MinIterations; i++)
        {
            var shopId = Guid.NewGuid();
            var taxRate = (decimal)(random.NextDouble() * 0.3); // 0–30%

            var sw = Stopwatch.StartNew();
            await _cacheService.SetTaxRateAsync(shopId, taxRate);
            sw.Stop();
            if (sw.ElapsedMilliseconds > SaleOperationThresholdMs)
                violations.Add(("SetTaxRate", sw.ElapsedMilliseconds));

            sw.Restart();
            var cached = await _cacheService.GetTaxRateAsync(shopId);
            sw.Stop();
            if (sw.ElapsedMilliseconds > SaleOperationThresholdMs)
                violations.Add(("GetTaxRate", sw.ElapsedMilliseconds));

            Assert.NotNull(cached);
            Assert.Equal(taxRate, cached!.Value);
            passCount++;
        }

        _output.WriteLine($"Passed {passCount}/{MinIterations} tax rate cache iterations");
        Assert.Empty(violations);
    }

    /// <summary>
    /// **Validates: Requirements 9.1, 9.5**
    ///
    /// Property 17: Performance Response Time (active sale cache)
    /// For any active sale cache operation, the operation must complete within 200ms.
    /// </summary>
    [Fact]
    public async Task Property17_ActiveSaleCacheOperations_AlwaysCompleteWithin200ms()
    {
        var random = new Random(42);
        var violations = new List<(string Op, long Ms)>();
        int passCount = 0;

        for (int i = 0; i < MinIterations; i++)
        {
            var sale = GenerateRandomSale(random);

            var sw = Stopwatch.StartNew();
            await _cacheService.SetActiveSaleAsync(sale);
            sw.Stop();
            if (sw.ElapsedMilliseconds > SaleOperationThresholdMs)
                violations.Add(("SetActiveSale", sw.ElapsedMilliseconds));

            sw.Restart();
            var cached = await _cacheService.GetActiveSaleAsync(sale.Id);
            sw.Stop();
            if (sw.ElapsedMilliseconds > SaleOperationThresholdMs)
                violations.Add(("GetActiveSale", sw.ElapsedMilliseconds));

            Assert.NotNull(cached);
            passCount++;
        }

        _output.WriteLine($"Passed {passCount}/{MinIterations} active sale cache iterations");
        Assert.Empty(violations);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Product GenerateRandomProduct(Random rng) => new()
    {
        Id        = Guid.NewGuid(),
        ShopId    = Guid.NewGuid(),
        Name      = $"Product-{rng.Next(1000, 9999)}",
        Barcode   = $"BAR-{rng.Next(100000, 999999)}",
        UnitPrice = (decimal)(rng.NextDouble() * 1000),
        IsActive  = true
    };

    private static Sale GenerateRandomSale(Random rng) => new()
    {
        Id            = Guid.NewGuid(),
        ShopId        = Guid.NewGuid(),
        UserId        = Guid.NewGuid(),
        InvoiceNumber = $"INV-{rng.Next(100000, 999999)}",
        TotalAmount   = (decimal)(rng.NextDouble() * 10000),
        CreatedAt     = DateTime.UtcNow
    };

    public void Dispose()
    {
        _guard.Dispose();
        _sp.Dispose();
    }
}
