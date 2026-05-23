using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Services;
using System.Collections.ObjectModel;

namespace Desktop.ViewModels;

/// <summary>
/// ViewModel for configuration management
/// </summary>
public partial class ConfigurationViewModel : BaseViewModel
{
    private readonly IConfigurationService _configurationService;
    private readonly ICurrentUserService _currentUserService;

    [ObservableProperty]
    private ShopPricingSettings shopPricingSettings = new();

    [ObservableProperty]
    private ShopTaxSettings shopTaxSettings = new();

    [ObservableProperty]
    private UserPreferences userPreferences = new();

    [ObservableProperty]
    private BarcodeScannerSettings barcodeScannerSettings = new();

    [ObservableProperty]
    private PerformanceSettings performanceSettings = new();

    [ObservableProperty]
    private BusinessSettings businessSettings = new();

    [ObservableProperty]
    private CurrencySettings currencySettings = new();

    [ObservableProperty]
    private LocalizationSettings localizationSettings = new();

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string selectedTab = "Shop";

    public ObservableCollection<string> AvailableThemes { get; } = new() { "Light", "Dark", "Auto" };
    public ObservableCollection<string> AvailableLanguages { get; } = new() { "en", "bn", "es", "fr", "de", "zh" };
    public ObservableCollection<string> AvailableFontFamilies { get; } = new() { "Segoe UI", "Arial", "Calibri", "Tahoma" };
    public ObservableCollection<string> AvailableScannerTypes { get; } = new() { "Camera", "USB", "Bluetooth" };
    public ObservableCollection<string> AvailableScanRegions { get; } = new() { "Center", "FullScreen", "Custom" };

    public ConfigurationViewModel(
        IConfigurationService configurationService,
        ICurrentUserService currentUserService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        Title = "Configuration";
        _ = Task.Run(LoadConfigurationsAsync);
    }

    [RelayCommand]
    private async Task LoadConfigurationsAsync()
    {
        IsBusy = true;
        StatusMessage = "Loading configurations...";

        try
        {
            var currentUser = _currentUserService.CurrentUser;

            if (currentUser?.ShopId != null)
            {
                ShopPricingSettings = await _configurationService.GetShopPricingSettingsAsync(currentUser.ShopId.Value);
                ShopTaxSettings = await _configurationService.GetShopTaxSettingsAsync(currentUser.ShopId.Value);
            }

            if (currentUser != null)
            {
                UserPreferences = await _configurationService.GetUserPreferencesAsync(currentUser.Id);
            }

            BarcodeScannerSettings = await _configurationService.GetBarcodeScannerSettingsAsync();
            PerformanceSettings = await _configurationService.GetPerformanceSettingsAsync();
            BusinessSettings = await _configurationService.GetBusinessSettingsAsync();
            CurrencySettings = await _configurationService.GetCurrencySettingsAsync();
            LocalizationSettings = await _configurationService.GetLocalizationSettingsAsync();

            StatusMessage = "Configurations loaded successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading configurations: {ex.Message}";
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveShopPricingAsync()
    {
        IsBusy = true;
        StatusMessage = "Saving shop pricing settings...";

        try
        {
            var shopId = _currentUserService.CurrentUser?.ShopId;
            if (shopId == null)
            {
                StatusMessage = "No current shop selected";
                return;
            }

            await _configurationService.SetShopPricingSettingsAsync(shopId.Value, ShopPricingSettings);
            StatusMessage = "Shop pricing settings saved";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveShopTaxAsync()
    {
        IsBusy = true;
        StatusMessage = "Saving shop tax settings...";

        try
        {
            var shopId = _currentUserService.CurrentUser?.ShopId;
            if (shopId == null)
            {
                StatusMessage = "No current shop selected";
                return;
            }

            await _configurationService.SetShopTaxSettingsAsync(shopId.Value, ShopTaxSettings);
            StatusMessage = "Shop tax settings saved";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveUserPreferencesAsync()
    {
        IsBusy = true;
        StatusMessage = "Saving user preferences...";

        try
        {
            var currentUser = _currentUserService.CurrentUser;
            if (currentUser == null)
            {
                StatusMessage = "No current user";
                return;
            }

            UserPreferences.UserId = currentUser.Id;
            await _configurationService.SetUserPreferencesAsync(currentUser.Id, UserPreferences);
            StatusMessage = "User preferences saved";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveBarcodeScannerAsync()
    {
        IsBusy = true;
        StatusMessage = "Saving barcode scanner settings...";

        try
        {
            await _configurationService.SetBarcodeScannerSettingsAsync(BarcodeScannerSettings);
            StatusMessage = "Barcode scanner settings saved";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SavePerformanceAsync()
    {
        IsBusy = true;
        StatusMessage = "Saving performance settings...";

        try
        {
            await _configurationService.SetPerformanceSettingsAsync(PerformanceSettings);
            StatusMessage = "Performance settings saved";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        IsBusy = true;
        StatusMessage = "Resetting to defaults...";

        try
        {
            await _configurationService.ResetConfigurationAsync("Currency.Code");
            await _configurationService.ResetConfigurationAsync("Tax.DefaultRate");
            await _configurationService.ResetConfigurationAsync("Performance.PageSize");
            await LoadConfigurationsAsync();
            StatusMessage = "Configurations reset to defaults";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InitializeDefaultsAsync()
    {
        IsBusy = true;
        StatusMessage = "Initializing default configurations...";

        try
        {
            await _configurationService.InitializeDefaultConfigurationsAsync();
            await LoadConfigurationsAsync();
            StatusMessage = "Default configurations initialized";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
