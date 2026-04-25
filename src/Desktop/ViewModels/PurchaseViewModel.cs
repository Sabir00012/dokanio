using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Desktop.Models;
using Microsoft.Extensions.Logging;
using Shared.Core.Enums;
using Shared.Core.Repositories;
using Shared.Core.Services;
using System.Collections.ObjectModel;

namespace Desktop.ViewModels;

public partial class PurchaseViewModel : BaseViewModel
{
    private readonly IProductRepository _productRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IStockRepository _stockRepository;
    private readonly IInventoryUpdater _inventoryUpdater;
    private readonly ILogger<PurchaseViewModel> _logger;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private Supplier? selectedSupplier;

    [ObservableProperty]
    private string purchaseNumber = string.Empty;

    [ObservableProperty]
    private DateTime purchaseDate = DateTime.Today;

    public ObservableCollection<Supplier> Suppliers { get; } = new();
    public ObservableCollection<Product> SearchResults { get; } = new();
    public ObservableCollection<PurchaseItem> PurchaseItems { get; } = new();

    public decimal TotalAmount => PurchaseItems.Sum(item => item.Total);

    // Design-time constructor
    public PurchaseViewModel()
    {
        Title = "Purchase Entry";
        GeneratePurchaseNumber();
    }

    public PurchaseViewModel(
        IProductRepository productRepository,
        ISupplierRepository supplierRepository,
        IStockRepository stockRepository,
        IInventoryUpdater inventoryUpdater,
        ILogger<PurchaseViewModel> logger)
    {
        _productRepository = productRepository;
        _supplierRepository = supplierRepository;
        _stockRepository = stockRepository;
        _inventoryUpdater = inventoryUpdater;
        _logger = logger;
        Title = "Purchase Entry";
        GeneratePurchaseNumber();
        _ = Task.Run(LoadSuppliersAsync);
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchProductsAsync(value);
    }

    [RelayCommand]
    private async Task LoadSuppliersAsync()
    {
        try
        {
            if (_supplierRepository == null) return;
            var suppliers = await _supplierRepository.GetActiveSuppliersAsync();
            Suppliers.Clear();
            foreach (var s in suppliers)
            {
                Suppliers.Add(new Supplier
                {
                    Id = s.Id,
                    Name = s.Name,
                    ContactPerson = s.ContactPerson,
                    Phone = s.Phone,
                    Email = s.Email,
                    Address = s.Address,
                    IsActive = s.IsActive,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading suppliers");
            SetError($"Error loading suppliers: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SearchProductsAsync(string? searchTerm = null)
    {
        searchTerm ??= SearchText;
        SearchResults.Clear();

        if (string.IsNullOrWhiteSpace(searchTerm))
            return;

        try
        {
            if (_productRepository == null) return;
            var results = await _productRepository.SearchAsync(searchTerm);
            foreach (var p in results.Take(10))
            {
                SearchResults.Add(new Product
                {
                    Id = p.Id,
                    Name = p.Name,
                    Barcode = p.Barcode,
                    UnitPrice = p.UnitPrice,
                    Category = p.Category
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching products");
        }
    }

    [RelayCommand]
    private void AddProduct(Product product)
    {
        var existingItem = PurchaseItems.FirstOrDefault(item => item.ProductId == product.Id);

        if (existingItem != null)
        {
            existingItem.Quantity++;
        }
        else
        {
            PurchaseItems.Add(new PurchaseItem
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = 1,
                UnitCost = product.UnitPrice * 0.8m, // default 20% markup assumption
                BatchNumber = $"BATCH-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100, 999)}",
                ExpiryDate = DateTime.Today.AddMonths(24)
            });
        }

        SearchText = string.Empty;
        SearchResults.Clear();
        OnPropertyChanged(nameof(TotalAmount));
    }

    [RelayCommand]
    private void RemoveItem(PurchaseItem item)
    {
        PurchaseItems.Remove(item);
        OnPropertyChanged(nameof(TotalAmount));
    }

    [RelayCommand]
    private async Task CompletePurchase()
    {
        if (SelectedSupplier == null)
        {
            SetError("Please select a supplier");
            return;
        }

        if (!PurchaseItems.Any())
        {
            SetError("Please add items to the purchase");
            return;
        }

        IsBusy = true;
        ClearError();

        try
        {
            // Update stock for each purchased product
            foreach (var item in PurchaseItems)
            {
                if (_productRepository == null) break;

                var product = await _productRepository.GetByIdAsync(item.ProductId);
                if (product == null)
                {
                    _logger?.LogWarning("Product {ProductId} not found during purchase completion", item.ProductId);
                    continue;
                }

                // Update product batch/expiry if provided
                if (!string.IsNullOrWhiteSpace(item.BatchNumber))
                    product.BatchNumber = item.BatchNumber;
                if (item.ExpiryDate.HasValue)
                    product.ExpiryDate = item.ExpiryDate;
                product.PurchasePrice = item.UnitCost;
                product.UpdatedAt = DateTime.UtcNow;
                product.SyncStatus = SyncStatus.NotSynced;

                await _productRepository.UpdateAsync(product);

                // Update stock record
                if (_stockRepository != null)
                {
                    var stockEntries = await _stockRepository.FindAsync(s => s.ProductId == item.ProductId);
                    var stock = stockEntries.FirstOrDefault();
                    if (stock != null)
                    {
                        stock.Quantity += item.Quantity;
                        stock.UpdatedAt = DateTime.UtcNow;
                        stock.SyncStatus = SyncStatus.NotSynced;
                        await _stockRepository.UpdateAsync(stock);
                    }
                }
            }

            if (_productRepository != null)
                await _productRepository.SaveChangesAsync();

            _logger.LogInformation("Purchase {PurchaseNumber} completed with {ItemCount} items, total ₹{Total}",
                PurchaseNumber, PurchaseItems.Count, TotalAmount);

            SuccessMessage = $"Purchase completed! {PurchaseNumber} — ₹{TotalAmount:N2}";
            ResetPurchase();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing purchase");
            SetError($"Error completing purchase: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ResetPurchase()
    {
        PurchaseItems.Clear();
        SelectedSupplier = null;
        SearchText = string.Empty;
        SearchResults.Clear();
        ClearError();
        GeneratePurchaseNumber();
        PurchaseDate = DateTime.Today;
        OnPropertyChanged(nameof(TotalAmount));
    }

    private void GeneratePurchaseNumber()
    {
        PurchaseNumber = $"PUR-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
    }
}
