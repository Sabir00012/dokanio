using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Shared.Core.Enums;
using Shared.Core.Repositories;
using System.Collections.ObjectModel;

namespace Desktop.ViewModels;

public partial class ProductViewModel : BaseViewModel
{
    private readonly IProductRepository? _productRepository;
    private readonly IStockRepository? _stockRepository;
    private readonly ILogger<ProductViewModel>? _logger;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private Desktop.Models.Product? selectedProduct;

    [ObservableProperty]
    private bool isAddingProduct;

    [ObservableProperty]
    private string productName = string.Empty;

    [ObservableProperty]
    private string barcode = string.Empty;

    [ObservableProperty]
    private string category = string.Empty;

    [ObservableProperty]
    private decimal unitPrice;

    [ObservableProperty]
    private string batchNumber = string.Empty;

    [ObservableProperty]
    private DateTime? expiryDate;

    [ObservableProperty]
    private int stockQuantity;

    public ObservableCollection<Desktop.Models.Product> Products { get; } = new();
    public ObservableCollection<Desktop.Models.Product> FilteredProducts { get; } = new();
    public ObservableCollection<Desktop.Models.Product> ExpiringProducts { get; } = new();

    // Design-time constructor
    public ProductViewModel()
    {
        Title = "Product Management";
    }

    public ProductViewModel(
        IProductRepository productRepository,
        IStockRepository stockRepository,
        ILogger<ProductViewModel> logger)
    {
        _productRepository = productRepository;
        _stockRepository = stockRepository;
        _logger = logger;
        Title = "Product Management";
        _ = Task.Run(LoadProductsAsync);
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshFilteredProducts();
    }

    [RelayCommand]
    private async Task LoadProductsAsync()
    {
        if (_productRepository == null) return;

        IsBusy = true;
        ClearError();
        try
        {
            var entities = await _productRepository.GetActiveProductsAsync();
            Products.Clear();
            foreach (var e in entities)
                Products.Add(MapToDesktopModel(e));

            RefreshFilteredProducts();
            RefreshExpiringProducts();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading products");
            SetError($"Error loading products: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddNewProduct()
    {
        IsAddingProduct = true;
        ClearForm();
    }

    [RelayCommand]
    private void EditProduct(Desktop.Models.Product product)
    {
        SelectedProduct = product;
        IsAddingProduct = true;

        ProductName = product.Name;
        Barcode = product.Barcode ?? string.Empty;
        Category = product.Category ?? string.Empty;
        UnitPrice = product.UnitPrice;
        BatchNumber = product.BatchNumber ?? string.Empty;
        ExpiryDate = product.ExpiryDate;
        StockQuantity = product.StockQuantity;
    }

    [RelayCommand]
    private async Task SaveProduct()
    {
        if (string.IsNullOrWhiteSpace(ProductName))
        {
            SetError("Product name is required");
            return;
        }

        if (UnitPrice <= 0)
        {
            SetError("Unit price must be greater than zero");
            return;
        }

        IsBusy = true;
        ClearError();

        try
        {
            if (SelectedProduct != null && _productRepository != null)
            {
                // Update existing entity
                var entity = await _productRepository.GetByIdAsync(SelectedProduct.Id);
                if (entity != null)
                {
                    entity.Name = ProductName;
                    entity.Barcode = string.IsNullOrWhiteSpace(Barcode) ? null : Barcode;
                    entity.Category = Category;
                    entity.UnitPrice = UnitPrice;
                    entity.BatchNumber = string.IsNullOrWhiteSpace(BatchNumber) ? null : BatchNumber;
                    entity.ExpiryDate = ExpiryDate;
                    entity.UpdatedAt = DateTime.UtcNow;
                    entity.SyncStatus = SyncStatus.NotSynced;

                    await _productRepository.UpdateAsync(entity);
                    await _productRepository.SaveChangesAsync();

                    // Update the desktop model in-place
                    SelectedProduct.Name = ProductName;
                    SelectedProduct.Barcode = Barcode;
                    SelectedProduct.Category = Category;
                    SelectedProduct.UnitPrice = UnitPrice;
                    SelectedProduct.BatchNumber = BatchNumber;
                    SelectedProduct.ExpiryDate = ExpiryDate;
                    SelectedProduct.StockQuantity = StockQuantity;
                    SelectedProduct.UpdatedAt = DateTime.UtcNow;

                    _logger?.LogInformation("Updated product {ProductName}", ProductName);
                }
            }
            else if (_productRepository != null)
            {
                // Create new entity
                var entity = new Shared.Core.Entities.Product
                {
                    Id = Guid.NewGuid(),
                    Name = ProductName,
                    Barcode = string.IsNullOrWhiteSpace(Barcode) ? null : Barcode,
                    Category = Category,
                    UnitPrice = UnitPrice,
                    BatchNumber = string.IsNullOrWhiteSpace(BatchNumber) ? null : BatchNumber,
                    ExpiryDate = ExpiryDate,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    SyncStatus = SyncStatus.NotSynced
                };

                await _productRepository.AddAsync(entity);
                await _productRepository.SaveChangesAsync();

                var desktopModel = MapToDesktopModel(entity);
                desktopModel.StockQuantity = StockQuantity;
                Products.Add(desktopModel);

                _logger?.LogInformation("Created product {ProductName}", ProductName);
            }

            RefreshFilteredProducts();
            RefreshExpiringProducts();
            CancelEdit();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving product");
            SetError($"Error saving product: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsAddingProduct = false;
        SelectedProduct = null;
        ClearForm();
    }

    [RelayCommand]
    private async Task DeleteProduct(Desktop.Models.Product product)
    {
        if (_productRepository == null) return;

        IsBusy = true;
        ClearError();

        try
        {
            var entity = await _productRepository.GetByIdAsync(product.Id);
            if (entity != null)
            {
                entity.IsActive = false;
                entity.IsDeleted = true;
                entity.DeletedAt = DateTime.UtcNow;
                entity.UpdatedAt = DateTime.UtcNow;
                entity.SyncStatus = SyncStatus.NotSynced;

                await _productRepository.UpdateAsync(entity);
                await _productRepository.SaveChangesAsync();
            }

            product.IsActive = false;
            RefreshFilteredProducts();
            RefreshExpiringProducts();

            _logger?.LogInformation("Soft-deleted product {ProductName}", product.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error deleting product");
            SetError($"Error deleting product: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearForm()
    {
        ProductName = string.Empty;
        Barcode = string.Empty;
        Category = string.Empty;
        UnitPrice = 0;
        BatchNumber = string.Empty;
        ExpiryDate = null;
        StockQuantity = 0;
        ClearError();
    }

    private void RefreshFilteredProducts()
    {
        FilteredProducts.Clear();

        var filtered = Products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchLower = SearchText.ToLowerInvariant();
            filtered = filtered.Where(p =>
                p.Name.ToLowerInvariant().Contains(searchLower) ||
                (p.Barcode?.Contains(SearchText) == true) ||
                (p.Category?.ToLowerInvariant().Contains(searchLower) == true));
        }

        foreach (var product in filtered.OrderBy(p => p.Name))
            FilteredProducts.Add(product);
    }

    private void RefreshExpiringProducts()
    {
        ExpiringProducts.Clear();

        var threshold = DateTime.Today.AddDays(30);
        var expiring = Products
            .Where(p => p.IsActive &&
                        p.ExpiryDate.HasValue &&
                        p.ExpiryDate.Value <= threshold &&
                        p.ExpiryDate.Value >= DateTime.Today)
            .OrderBy(p => p.ExpiryDate);

        foreach (var product in expiring)
            ExpiringProducts.Add(product);
    }

    private static Desktop.Models.Product MapToDesktopModel(Shared.Core.Entities.Product entity)
    {
        return new Desktop.Models.Product
        {
            Id = entity.Id,
            Name = entity.Name,
            Barcode = entity.Barcode,
            Category = entity.Category,
            UnitPrice = entity.UnitPrice,
            BatchNumber = entity.BatchNumber,
            ExpiryDate = entity.ExpiryDate,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            StockQuantity = 0 // populated separately from Stock entity if needed
        };
    }
}
