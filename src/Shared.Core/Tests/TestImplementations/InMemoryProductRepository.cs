using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Shared.Core.Data;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Repositories;

namespace Shared.Core.Tests.TestImplementations;

/// <summary>
/// In-memory implementation of IProductRepository for testing purposes
/// </summary>
public class InMemoryProductRepository : IProductRepository
{
    private readonly PosDbContext _context;

    public InMemoryProductRepository(PosDbContext context)
    {
        _context = context;
    }

    public async Task<Product?> GetByIdAsync(Guid id)
    {
        return await _context.Products.FindAsync(id);
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        return await _context.Products.ToListAsync();
    }

    public async Task<IEnumerable<Product>> FindAsync(Expression<Func<Product, bool>> predicate)
    {
        return await _context.Products.Where(predicate).ToListAsync();
    }

    public async Task AddAsync(Product entity)
    {
        await _context.Products.AddAsync(entity);
    }

    public async Task UpdateAsync(Product entity)
    {
        _context.Products.Update(entity);
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product != null)
        {
            _context.Products.Remove(product);
        }
    }

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    // ── Transaction support (Requirement 9.3) ─────────────────────────────────

    public Task<IDbContextTransaction> BeginTransactionAsync()
        => _context.Database.BeginTransactionAsync();

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try { var r = await operation(); await tx.CommitAsync(); return r; }
            catch { await tx.RollbackAsync(); throw; }
        });
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation)
    {
        await ExecuteInTransactionAsync(async () => { await operation(); return true; });
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode)
    {
        return await _context.Products
            .FirstOrDefaultAsync(p => p.Barcode == barcode);
    }

    public async Task<IEnumerable<Product>> GetActiveByCategoryAsync(string category)
    {
        return await _context.Products
            .Where(p => p.IsActive && p.Category == category)
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> GetExpiringMedicinesAsync(DateTime beforeDate)
    {
        return await _context.Products
            .Where(p => p.ExpiryDate.HasValue && p.ExpiryDate < beforeDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> GetActiveProductsAsync()
    {
        return await _context.Products
            .Where(p => p.IsActive)
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> GetUnsyncedAsync()
    {
        return await _context.Products
            .Where(p => p.SyncStatus == SyncStatus.NotSynced)
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> SearchAsync(string searchTerm)
    {
        return await _context.Products
            .Where(p => p.Name.Contains(searchTerm) || (p.Barcode != null && p.Barcode.Contains(searchTerm)))
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> GetProductsByShopAsync(Guid shopId)
    {
        return await _context.Products
            .Where(p => p.ShopId == shopId && p.IsActive)
            .ToListAsync();
    }

    public async Task<PagedResult<Product>> SearchProductsPagedAsync(
        Guid shopId,
        string? searchTerm = null,
        int page = 0,
        int pageSize = 20)
    {
        if (page < 0) page = 0;
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _context.Products
            .Where(p => p.ShopId == shopId && p.IsActive && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var lower = searchTerm.ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(lower) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(lower)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Name)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return PagedResult<Product>.Create(items, totalCount, page, pageSize);
    }

    // ── Enhanced query methods (Requirement 9.3, 9.4) ─────────────────────────

    public async Task<Product?> GetProductWithStockAsync(Guid productId)
    {
        return await _context.Products
            .Include(p => p.StockEntries)
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);
    }

    public async Task<Dictionary<Guid, Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds?.ToList() ?? new List<Guid>();
        if (ids.Count == 0) return new Dictionary<Guid, Product>();

        var products = await _context.Products
            .Where(p => ids.Contains(p.Id) && !p.IsDeleted)
            .ToListAsync();

        return products.ToDictionary(p => p.Id);
    }

    public async Task<IEnumerable<Product>> GetWeightBasedProductsAsync(Guid shopId)
    {
        return await _context.Products
            .Where(p => p.ShopId == shopId && p.IsActive && p.IsWeightBased && !p.IsDeleted)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }
}