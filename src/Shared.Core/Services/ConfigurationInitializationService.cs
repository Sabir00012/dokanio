using Microsoft.Extensions.Logging;
using Shared.Core.DTOs;

namespace Shared.Core.Services;

/// <summary>
/// Service implementation for initializing configuration settings
/// </summary>
public class ConfigurationInitializationService : IConfigurationInitializationService
{
    private readonly IConfigurationService _configurationService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<ConfigurationInitializationService> _logger;

    public ConfigurationInitializationService(
        IConfigurationService configurationService,
        IFeatureFlagService featureFlagService,
        ILogger<ConfigurationInitializationService> logger)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _featureFlagService = featureFlagService ?? throw new ArgumentNullException(nameof(featureFlagService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes all default configuration settings
    /// </summary>
    /// <returns>Task</returns>
    public async Task InitializeDefaultConfigurationsAsync()
    {
        try
        {
            _logger.LogInformation("Initializing default configuration settings");

            // Initialize default system configurations
            await _configurationService.InitializeDefaultConfigurationsAsync();

            // Initialize default feature flags
            await _featureFlagService.InitializeDefaultFeatureFlagsAsync();

            // Initialize default performance settings
            await InitializeDefaultPerformanceSettingsAsync();

            _logger.LogInformation("Default configuration settings initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing default configuration settings");
            throw;
        }
    }

    /// <summary>
    /// Initializes configuration for a new shop
    /// </summary>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>Task</returns>
    public async Task InitializeShopConfigurationAsync(Guid shopId)
    {
        try
        {
            _logger.LogInformation("Initializing configuration for shop {ShopId}", shopId);

            // Initialize shop pricing settings
            var defaultPricingSettings = new ShopPricingSettings();
            await _configurationService.SetShopPricingSettingsAsync(shopId, defaultPricingSettings);

            // Initialize shop tax settings
            var defaultTaxSettings = new ShopTaxSettings();
            await _configurationService.SetShopTaxSettingsAsync(shopId, defaultTaxSettings);

            // Initialize shop calculation precision settings
            var defaultPrecisionSettings = new SalesCalculationPrecisionSettings();
            await _configurationService.SetSalesCalculationPrecisionSettingsAsync(shopId, defaultPrecisionSettings);

            _logger.LogInformation("Configuration initialized successfully for shop {ShopId}", shopId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing configuration for shop {ShopId}", shopId);
            throw;
        }
    }

    /// <summary>
    /// Initializes configuration for a new user
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <returns>Task</returns>
    public async Task InitializeUserConfigurationAsync(Guid userId)
    {
        try
        {
            _logger.LogInformation("Initializing configuration for user {UserId}", userId);

            // Initialize user preferences
            var defaultPreferences = new UserPreferences
            {
                UserId = userId
            };
            await _configurationService.SetUserPreferencesAsync(userId, defaultPreferences);

            // Initialize user calculation preferences
            var defaultCalculationPreferences = new UserCalculationPreferences
            {
                UserId = userId
            };
            await _configurationService.SetUserCalculationPreferencesAsync(userId, defaultCalculationPreferences);

            _logger.LogInformation("Configuration initialized successfully for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing configuration for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Validates all configuration settings
    /// </summary>
    /// <returns>True if all configurations are valid</returns>
    public async Task<bool> ValidateAllConfigurationsAsync()
    {
        try
        {
            _logger.LogInformation("Validating all configuration settings");

            var isValid = true;

            // Validate system configurations
            var systemConfigs = await _configurationService.GetSystemConfigurationsAsync();
            foreach (var config in systemConfigs)
            {
                var validationResult = await _configurationService.ValidateConfigurationAsync(config.Key, config.Value, config.Type);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Invalid configuration: {Key} = {Value}, Error: {Error}", 
                        config.Key, config.Value, validationResult.ErrorMessage);
                    isValid = false;
                }
            }

            // Validate performance settings
            var performanceSettings = await _configurationService.GetSalesPerformanceSettingsAsync();
            if (performanceSettings.CalculationTimeoutMs <= 0 || performanceSettings.CalculationTimeoutMs > 10000)
            {
                _logger.LogWarning("Invalid calculation timeout: {Timeout}ms", performanceSettings.CalculationTimeoutMs);
                isValid = false;
            }

            if (performanceSettings.MaxConcurrentSales <= 0 || performanceSettings.MaxConcurrentSales > 100)
            {
                _logger.LogWarning("Invalid max concurrent sales: {MaxConcurrent}", performanceSettings.MaxConcurrentSales);
                isValid = false;
            }

            _logger.LogInformation("Configuration validation completed. Valid: {IsValid}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating configuration settings");
            return false;
        }
    }

    /// <summary>
    /// Migrates configuration settings to new version
    /// </summary>
    /// <param name="fromVersion">Source version</param>
    /// <param name="toVersion">Target version</param>
    /// <returns>Task</returns>
    public async Task MigrateConfigurationAsync(string fromVersion, string toVersion)
    {
        try
        {
            _logger.LogInformation("Migrating configuration from version {FromVersion} to {ToVersion}", fromVersion, toVersion);

            // Add version-specific migration logic here
            switch (fromVersion)
            {
                case "1.0":
                    await MigrateFrom1_0To1_1Async();
                    break;
                case "1.1":
                    await MigrateFrom1_1To1_2Async();
                    break;
                default:
                    _logger.LogWarning("No migration path defined from version {FromVersion}", fromVersion);
                    break;
            }

            _logger.LogInformation("Configuration migration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating configuration from version {FromVersion} to {ToVersion}", fromVersion, toVersion);
            throw;
        }
    }

    private async Task InitializeDefaultPerformanceSettingsAsync()
    {
        var defaultSettings = new SalesPerformanceSettings();
        await _configurationService.SetSalesPerformanceSettingsAsync(defaultSettings);
    }

    private async Task MigrateFrom1_0To1_1Async()
    {
        // Example migration: Add new feature flags introduced in version 1.1
        var featureFlags = new FeatureFlagSettings
        {
            EnhancedRealTimeCalculations = true,
            AdvancedDiscountProcessing = true,
            // Set other flags to false for gradual rollout
            MultiCurrencySupport = false,
            AIPoweredRecommendations = false
        };
        
        await _configurationService.SetFeatureFlagSettingsAsync(featureFlags);
    }

    private async Task MigrateFrom1_1To1_2Async()
    {
        // Example migration: Update performance settings for version 1.2
        var performanceSettings = await _configurationService.GetSalesPerformanceSettingsAsync();
        
        // Update timeout values for better performance
        performanceSettings.CalculationTimeoutMs = Math.Min(performanceSettings.CalculationTimeoutMs, 100);
        performanceSettings.ValidationTimeoutMs = Math.Min(performanceSettings.ValidationTimeoutMs, 50);
        
        await _configurationService.SetSalesPerformanceSettingsAsync(performanceSettings);
    }
}