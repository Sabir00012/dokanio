using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Repositories;
using System.Collections.ObjectModel;

namespace Desktop.ViewModels;

public partial class SupplierViewModel : BaseViewModel
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly ILogger<SupplierViewModel> _logger;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private Supplier? selectedSupplier;

    [ObservableProperty]
    private bool isAddingSupplier;

    [ObservableProperty]
    private string supplierName = string.Empty;

    [ObservableProperty]
    private string contactPerson = string.Empty;

    [ObservableProperty]
    private string phone = string.Empty;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string address = string.Empty;

    public ObservableCollection<Supplier> Suppliers { get; } = new();
    public ObservableCollection<Supplier> FilteredSuppliers { get; } = new();

    // Design-time constructor
    public SupplierViewModel()
    {
        Title = "Supplier Management";
    }

    public SupplierViewModel(
        ISupplierRepository supplierRepository,
        ILogger<SupplierViewModel> logger)
    {
        _supplierRepository = supplierRepository;
        _logger = logger;
        Title = "Supplier Management";
        _ = Task.Run(LoadSuppliersAsync);
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshFilteredSuppliers();
    }

    [RelayCommand]
    private async Task LoadSuppliersAsync()
    {
        IsBusy = true;
        ClearError();
        try
        {
            var suppliers = await _supplierRepository.GetActiveSuppliersAsync();
            Suppliers.Clear();
            foreach (var s in suppliers)
                Suppliers.Add(s);

            RefreshFilteredSuppliers();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading suppliers");
            SetError($"Error loading suppliers: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddNewSupplier()
    {
        IsAddingSupplier = true;
        ClearForm();
    }

    [RelayCommand]
    private void EditSupplier(Supplier supplier)
    {
        SelectedSupplier = supplier;
        IsAddingSupplier = true;

        SupplierName = supplier.Name;
        ContactPerson = supplier.ContactPerson ?? string.Empty;
        Phone = supplier.Phone ?? string.Empty;
        Email = supplier.Email ?? string.Empty;
        Address = supplier.Address ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveSupplier()
    {
        if (string.IsNullOrWhiteSpace(SupplierName))
        {
            SetError("Supplier name is required");
            return;
        }

        IsBusy = true;
        ClearError();

        try
        {
            if (SelectedSupplier != null)
            {
                SelectedSupplier.Name = SupplierName;
                SelectedSupplier.ContactPerson = ContactPerson;
                SelectedSupplier.Phone = Phone;
                SelectedSupplier.Email = Email;
                SelectedSupplier.Address = Address;
                SelectedSupplier.UpdatedAt = DateTime.UtcNow;
                SelectedSupplier.SyncStatus = SyncStatus.NotSynced;

                await _supplierRepository.UpdateAsync(SelectedSupplier);
                await _supplierRepository.SaveChangesAsync();

                _logger.LogInformation("Updated supplier {SupplierName}", SelectedSupplier.Name);
            }
            else
            {
                var newSupplier = new Supplier
                {
                    Id = Guid.NewGuid(),
                    Name = SupplierName,
                    ContactPerson = ContactPerson,
                    Phone = Phone,
                    Email = Email,
                    Address = Address,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    SyncStatus = SyncStatus.NotSynced
                };

                await _supplierRepository.AddAsync(newSupplier);
                await _supplierRepository.SaveChangesAsync();

                Suppliers.Add(newSupplier);
                _logger.LogInformation("Created supplier {SupplierName}", newSupplier.Name);
            }

            RefreshFilteredSuppliers();
            CancelEdit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving supplier");
            SetError($"Error saving supplier: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsAddingSupplier = false;
        SelectedSupplier = null;
        ClearForm();
    }

    [RelayCommand]
    private async Task DeleteSupplier(Supplier supplier)
    {
        IsBusy = true;
        ClearError();

        try
        {
            supplier.IsActive = false;
            supplier.IsDeleted = true;
            supplier.DeletedAt = DateTime.UtcNow;
            supplier.UpdatedAt = DateTime.UtcNow;
            supplier.SyncStatus = SyncStatus.NotSynced;

            await _supplierRepository.UpdateAsync(supplier);
            await _supplierRepository.SaveChangesAsync();

            RefreshFilteredSuppliers();
            _logger.LogInformation("Soft-deleted supplier {SupplierName}", supplier.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting supplier");
            SetError($"Error deleting supplier: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearForm()
    {
        SupplierName = string.Empty;
        ContactPerson = string.Empty;
        Phone = string.Empty;
        Email = string.Empty;
        Address = string.Empty;
        ClearError();
    }

    private void RefreshFilteredSuppliers()
    {
        FilteredSuppliers.Clear();

        var filtered = Suppliers.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchLower = SearchText.ToLowerInvariant();
            filtered = filtered.Where(s =>
                s.Name.ToLowerInvariant().Contains(searchLower) ||
                (s.ContactPerson?.ToLowerInvariant().Contains(searchLower) == true) ||
                (s.Phone?.Contains(SearchText) == true));
        }

        foreach (var supplier in filtered.OrderBy(s => s.Name))
            FilteredSuppliers.Add(supplier);
    }
}
