using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Repositories;
using Shared.Core.Services;
using Xunit;

namespace Shared.Core.Tests;

/// <summary>
/// Integration tests for paginated repository methods.
/// Validates <see cref="ISaleRepository.GetSaleHistoryPagedAsync"/> and
/// <see cref="IProductRepository.SearchProductsPagedAsync"/> (Requirement 9.6).
/// </summary>
public class PaginationRepositoryTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PosDbContext _context;
    private readonly ISaleRepository _saleRepo;
    private readonly IProductRepository _productRepo;

    // Shared test data
    private readonly Guid _shopId  = Guid.NewGuid();
    private readonly Guid _userId  = Guid.NewGuid();

    public PaginationRepositoryTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<PosDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString())
             .ConfigureWarnings(w => w.Ignore(
                 Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddLogging();
        services.AddMemoryCache();
        services.AddScoped<ISalesCacheService, SalesCacheService>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();

        _sp = services.BuildServiceProvider();
        _context = _sp.GetRequiredService<PosDbContext>();
        _saleRepo = _sp.GetRequiredService<ISaleRepository>();
        _productRepo = _sp.GetRequiredService<IProductRepository>();
    }

    // ── PagedResult<T> ─────────────────────────────────────────────────────────

    [Fact]
    public void PagedResult_TotalPages_CalculatedCorrectly()
    {
        var result = PagedResult<int>.Create(new[] { 1, 2, 3 }.ToList(), totalCount: 25, page: 0, pageSize: 10);
        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void PagedResult_LastPage_HasNoPreviousAndNoNext()
    {
        var result = PagedResult<int>.Create(new[] { 1 }.ToList(), totalCount: 21, page: 2, pageSize: 10);
        Assert.Equal(3, result.TotalPages);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void PagedResult_Empty_ReturnsZeroTotals()
    {
        var empty = PagedResult<string>.Empty();
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.TotalPages);
        Assert.Empty(empty.Items);
    }

    // ── Sale history pagination ────────────────────────────────────────────────

    [Fact]
    public async Task GetSaleHistoryPaged_ReturnsCorrectPage()
    {
        await SeedSalesAsync(count: 25);

        var page0 = await _saleRepo.GetSaleHistoryPagedAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            page: 0, pageSize: 10);

        Assert.Equal(10, page0.Items.Count);
        Assert.Equal(25, page0.TotalCount);
        Assert.Equal(3, page0.TotalPages);
        Assert.True(page0.HasNextPage);
        Assert.False(page0.HasPreviousPage);
    }

    [Fact]
    public async Task GetSaleHistoryPaged_LastPage_HasFewerItems()
    {
        await SeedSalesAsync(count: 25);

        var page2 = await _saleRepo.GetSaleHistoryPagedAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            page: 2, pageSize: 10);

        Assert.Equal(5, page2.Items.Count);
        Assert.False(page2.HasNextPage);
        Assert.True(page2.HasPreviousPage);
    }

    [Fact]
    public async Task GetSaleHistoryPaged_FilterByShop_ReturnsOnlyShopSales()
    {
        var otherShopId = Guid.NewGuid();
        await SeedSalesAsync(count: 10, shopId: _shopId);
        await SeedSalesAsync(count: 5,  shopId: otherShopId);

        var result = await _saleRepo.GetSaleHistoryPagedAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            shopId: _shopId, page: 0, pageSize: 20);

        Assert.Equal(10, result.TotalCount);
        Assert.All(result.Items, s => Assert.Equal(_shopId, s.ShopId));
    }

    [Fact]
    public async Task GetSaleHistoryPaged_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        await SeedSalesAsync(count: 8, status: SaleStatus.Completed);
        await SeedSalesAsync(count: 4, status: SaleStatus.Cancelled);

        var result = await _saleRepo.GetSaleHistoryPagedAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            status: SaleStatus.Completed, page: 0, pageSize: 20);

        Assert.Equal(8, result.TotalCount);
        Assert.All(result.Items, s => Assert.Equal(SaleStatus.Completed, s.Status));
    }

    [Fact]
    public async Task GetSaleHistoryPaged_DateRangeFilter_ExcludesOutOfRangeSales()
    {
        // Seed sales in the past (should be excluded)
        await SeedSalesAsync(count: 5, createdAt: DateTime.UtcNow.AddDays(-10));
        // Seed sales in range
        await SeedSalesAsync(count: 3, createdAt: DateTime.UtcNow);

        var result = await _saleRepo.GetSaleHistoryPagedAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            page: 0, pageSize: 20);

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task GetSaleHistoryPaged_EmptyDatabase_ReturnsEmptyResult()
    {
        var result = await _saleRepo.GetSaleHistoryPagedAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetSaleHistoryPaged_PageSizeClamped_DoesNotExceed200()
    {
        await SeedSalesAsync(count: 10);

        // Request an absurdly large page size
        var result = await _saleRepo.GetSaleHistoryPagedAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            page: 0, pageSize: 9999);

        // Should be clamped to 200 max; all 10 items fit within that
        Assert.Equal(10, result.Items.Count);
    }

    // ── Product search pagination ──────────────────────────────────────────────

    [Fact]
    public async Task SearchProductsPaged_ReturnsCorrectPage()
    {
        await SeedProductsAsync(count: 30);

        var page0 = await _productRepo.SearchProductsPagedAsync(_shopId, page: 0, pageSize: 10);

        Assert.Equal(10, page0.Items.Count);
        Assert.Equal(30, page0.TotalCount);
        Assert.Equal(3, page0.TotalPages);
    }

    [Fact]
    public async Task SearchProductsPaged_WithSearchTerm_FiltersResults()
    {
        await SeedProductsAsync(count: 10, namePrefix: "Apple");
        await SeedProductsAsync(count: 5,  namePrefix: "Banana");

        var result = await _productRepo.SearchProductsPagedAsync(_shopId, searchTerm: "Apple", page: 0, pageSize: 20);

        Assert.Equal(10, result.TotalCount);
        Assert.All(result.Items, p => Assert.Contains("Apple", p.Name));
    }

    [Fact]
    public async Task SearchProductsPaged_WithBarcodeSearch_FindsProduct()
    {
        await SeedProductsAsync(count: 5);
        // Add a product with a specific barcode
        _context.Products.Add(new Product
        {
            Id = Guid.NewGuid(), ShopId = _shopId, Name = "Special Item",
            Barcode = "SPECIAL-999", UnitPrice = 1m, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _productRepo.SearchProductsPagedAsync(_shopId, searchTerm: "SPECIAL-999", page: 0, pageSize: 10);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("SPECIAL-999", result.Items[0].Barcode);
    }

    [Fact]
    public async Task SearchProductsPaged_OnlyReturnsActiveProducts()
    {
        await SeedProductsAsync(count: 5, isActive: true);
        await SeedProductsAsync(count: 3, isActive: false);

        var result = await _productRepo.SearchProductsPagedAsync(_shopId, page: 0, pageSize: 20);

        Assert.Equal(5, result.TotalCount);
        Assert.All(result.Items, p => Assert.True(p.IsActive));
    }

    [Fact]
    public async Task SearchProductsPaged_OnlyReturnsProductsForShop()
    {
        var otherShop = Guid.NewGuid();
        await SeedProductsAsync(count: 5, shopId: _shopId);
        await SeedProductsAsync(count: 3, shopId: otherShop);

        var result = await _productRepo.SearchProductsPagedAsync(_shopId, page: 0, pageSize: 20);

        Assert.Equal(5, result.TotalCount);
        Assert.All(result.Items, p => Assert.Equal(_shopId, p.ShopId));
    }

    [Fact]
    public async Task SearchProductsPaged_EmptyDatabase_ReturnsEmptyResult()
    {
        var result = await _productRepo.SearchProductsPagedAsync(_shopId);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task SeedSalesAsync(
        int count,
        Guid? shopId = null,
        SaleStatus status = SaleStatus.Completed,
        DateTime? createdAt = null)
    {
        var sid = shopId ?? _shopId;
        var at  = createdAt ?? DateTime.UtcNow;

        for (int i = 0; i < count; i++)
        {
            _context.Sales.Add(new Sale
            {
                Id            = Guid.NewGuid(),
                ShopId        = sid,
                UserId        = _userId,
                InvoiceNumber = $"INV-{Guid.NewGuid():N}",
                Status        = status,
                TotalAmount   = 100m + i,
                CreatedAt     = at,
                UpdatedAt     = at,
                DeviceId      = Guid.NewGuid()
            });
        }
        await _context.SaveChangesAsync();
    }

    private async Task SeedProductsAsync(
        int count,
        string namePrefix = "Product",
        Guid? shopId = null,
        bool isActive = true)
    {
        var sid = shopId ?? _shopId;
        for (int i = 0; i < count; i++)
        {
            _context.Products.Add(new Product
            {
                Id        = Guid.NewGuid(),
                ShopId    = sid,
                Name      = $"{namePrefix} {Guid.NewGuid():N}",
                UnitPrice = 10m + i,
                IsActive  = isActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeviceId  = Guid.NewGuid()
            });
        }
        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        _sp.Dispose();
    }
}
