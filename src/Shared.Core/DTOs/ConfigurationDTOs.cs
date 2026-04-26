using Shared.Core.Enums;

namespace Shared.Core.DTOs;

/// <summary>
/// DTO for configuration data transfer
/// </summary>
public class ConfigurationDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public ConfigurationType Type { get; set; }
    public string? Description { get; set; }
    public bool IsSystemLevel { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid DeviceId { get; set; }
    public Guid? ShopId { get; set; }
    public Guid? UserId { get; set; }
    public SyncStatus SyncStatus { get; set; }
}

/// <summary>
/// Request for creating or updating a configuration
/// </summary>
public class ConfigurationRequest
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public ConfigurationType Type { get; set; }
    public string? Description { get; set; }
    public bool IsSystemLevel { get; set; }
}

/// <summary>
/// Response for configuration validation
/// </summary>
public class ConfigurationValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public object? ParsedValue { get; set; }
}

/// <summary>
/// Currency settings configuration
/// </summary>
public class CurrencySettings
{
    public string CurrencyCode { get; set; } = "USD";
    public string CurrencySymbol { get; set; } = "$";
    public int DecimalPlaces { get; set; } = 2;
    public string DecimalSeparator { get; set; } = ".";
    public string ThousandsSeparator { get; set; } = ",";
    public bool SymbolBeforeAmount { get; set; } = true;
}

/// <summary>
/// Tax settings configuration
/// </summary>
public class TaxSettings
{
    public bool TaxEnabled { get; set; } = true;
    public decimal DefaultTaxRate { get; set; } = 0.0m;
    public string TaxName { get; set; } = "Tax";
    public bool TaxIncludedInPrice { get; set; } = false;
    public bool ShowTaxOnReceipt { get; set; } = true;
}

/// <summary>
/// Business settings configuration
/// </summary>
public class BusinessSettings
{
    public string BusinessName { get; set; } = string.Empty;
    public string BusinessAddress { get; set; } = string.Empty;
    public string BusinessPhone { get; set; } = string.Empty;
    public string BusinessEmail { get; set; } = string.Empty;
    public string BusinessWebsite { get; set; } = string.Empty;
    public string BusinessLogo { get; set; } = string.Empty;
    public string ReceiptFooter { get; set; } = string.Empty;
}

/// <summary>
/// Localization settings configuration
/// </summary>
public class LocalizationSettings
{
    public string Language { get; set; } = "en";
    public string Country { get; set; } = "US";
    public string TimeZone { get; set; } = "UTC";
    public string DateFormat { get; set; } = "MM/dd/yyyy";
    public string TimeFormat { get; set; } = "HH:mm:ss";
    public string NumberFormat { get; set; } = "N2";
}

/// <summary>
/// Shop-level pricing rules configuration
/// </summary>
public class ShopPricingSettings
{
    public bool WeightBasedPricingEnabled { get; set; } = true;
    public bool BulkDiscountEnabled { get; set; } = false;
    public decimal BulkDiscountThreshold { get; set; } = 10.0m;
    public decimal BulkDiscountPercentage { get; set; } = 5.0m;
    public bool MembershipPricingEnabled { get; set; } = true;
    public bool DynamicPricingEnabled { get; set; } = false;
    public decimal MinimumProfitMargin { get; set; } = 10.0m;
    public bool RoundingEnabled { get; set; } = true;
    public decimal RoundingPrecision { get; set; } = 0.05m;
}

/// <summary>
/// Shop-level tax configuration
/// </summary>
public class ShopTaxSettings
{
    public bool TaxEnabled { get; set; } = true;
    public decimal DefaultTaxRate { get; set; } = 0.0m;
    public string TaxName { get; set; } = "Tax";
    public bool TaxIncludedInPrice { get; set; } = false;
    public bool ShowTaxOnReceipt { get; set; } = true;
    public Dictionary<string, decimal> CategoryTaxRates { get; set; } = new();
    public bool CompoundTaxEnabled { get; set; } = false;
    public List<TaxRule> TaxRules { get; set; } = new();
}

/// <summary>
/// Tax rule for specific conditions
/// </summary>
public class TaxRule
{
    public string Name { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public decimal TaxRate { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// User preferences for UI customization
/// </summary>
public class UserPreferences
{
    public Guid UserId { get; set; }
    public string Theme { get; set; } = "Light";
    public string AccentColor { get; set; } = "#0078D4";
    public int FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Segoe UI";
    public bool HighContrastMode { get; set; } = false;
    public bool ReducedMotion { get; set; } = false;
    public string DefaultView { get; set; } = "Sales";
    public bool ShowTooltips { get; set; } = true;
    public bool AutoSaveEnabled { get; set; } = true;
    public int AutoSaveInterval { get; set; } = 30; // seconds
    public bool SoundEnabled { get; set; } = true;
    public bool HapticFeedbackEnabled { get; set; } = true;
    public string Language { get; set; } = "en";
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

/// <summary>
/// Barcode scanner configuration options
/// </summary>
public class BarcodeScannerSettings
{
    public bool ScannerEnabled { get; set; } = true;
    public string ScannerType { get; set; } = "Camera"; // Camera, USB, Bluetooth
    public List<string> SupportedFormats { get; set; } = new() { "EAN13", "EAN8", "UPC", "Code128", "Code39" };
    public bool AutoFocusEnabled { get; set; } = true;
    public bool FlashlightEnabled { get; set; } = false;
    public bool BeepOnScanEnabled { get; set; } = true;
    public bool VibrateOnScanEnabled { get; set; } = true;
    public int ScanTimeoutSeconds { get; set; } = 10;
    public bool ContinuousScanMode { get; set; } = false;
    public string ScanRegion { get; set; } = "Center"; // Center, FullScreen, Custom
    public double ScanRegionWidth { get; set; } = 0.8;
    public double ScanRegionHeight { get; set; } = 0.6;
    public bool ShowScanOverlay { get; set; } = true;
    public string OverlayColor { get; set; } = "#FF0000";
    public bool ValidateChecksum { get; set; } = true;
    public int MinBarcodeLength { get; set; } = 4;
    public int MaxBarcodeLength { get; set; } = 50;
}

/// <summary>
/// Performance tuning settings
/// </summary>
public class PerformanceSettings
{
    public int DatabaseConnectionPoolSize { get; set; } = 10;
    public int DatabaseCommandTimeoutSeconds { get; set; } = 30;
    public bool DatabaseQueryCachingEnabled { get; set; } = true;
    public int CacheExpirationMinutes { get; set; } = 15;
    public int MaxCacheSize { get; set; } = 100; // MB
    public bool LazyLoadingEnabled { get; set; } = true;
    public int PageSize { get; set; } = 50;
    public bool BackgroundSyncEnabled { get; set; } = true;
    public int SyncIntervalMinutes { get; set; } = 5;
    public int MaxConcurrentOperations { get; set; } = 5;
    public bool CompressionEnabled { get; set; } = true;
    public bool ImageOptimizationEnabled { get; set; } = true;
    public int MaxImageSizeKB { get; set; } = 500;
    public bool PreloadCriticalData { get; set; } = true;
    public int UIUpdateThrottleMs { get; set; } = 100;
    public bool MemoryOptimizationEnabled { get; set; } = true;
    public int GarbageCollectionThresholdMB { get; set; } = 50;
}
/// <summary>
/// Configuration export data for backup and migration
/// </summary>
public class ConfigurationExport
{
    public Guid ShopId { get; set; }
    public DateTime ExportedAt { get; set; }
    public ShopPricingSettings? PricingSettings { get; set; }
    public ShopTaxSettings? TaxSettings { get; set; }
    public BusinessSettings? BusinessSettings { get; set; }
    public CurrencySettings? CurrencySettings { get; set; }
    public LocalizationSettings? LocalizationSettings { get; set; }
    public string Version { get; set; } = "1.0";
}

/// <summary>
/// Configuration validation summary
/// </summary>
public class ConfigurationValidationSummary
{
    public Guid ShopId { get; set; }
    public DateTime ValidatedAt { get; set; }
    public bool IsValid { get; set; }
    public List<ConfigurationValidationResult> ValidationResults { get; set; } = new();
}

/// <summary>
/// Configuration recommendations based on analytics
/// </summary>
public class ConfigurationRecommendations
{
    public Guid ShopId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public List<ConfigurationRecommendation> Recommendations { get; set; } = new();
}

/// <summary>
/// Individual configuration recommendation
/// </summary>
public class ConfigurationRecommendation
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RecommendationPriority Priority { get; set; }
    public string RecommendedValue { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public decimal? EstimatedImpact { get; set; }
}

/// <summary>
/// Priority levels for configuration recommendations
/// </summary>
public enum RecommendationPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Feature flag configuration
/// </summary>
public class FeatureFlagConfiguration
{
    public FeatureFlag FeatureFlag { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int RolloutPercentage { get; set; } = 100;
    public DateTime? EnabledAt { get; set; }
    public DateTime? DisabledAt { get; set; }
    public Guid? ShopId { get; set; }
    public Guid? UserId { get; set; }
    public FeatureFlagScope Scope { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Feature flag scope
/// </summary>
public enum FeatureFlagScope
{
    Global,
    Shop,
    User
}

/// <summary>
/// Feature flag analytics data
/// </summary>
public class FeatureFlagAnalytics
{
    public FeatureFlag FeatureFlag { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int TotalUsers { get; set; }
    public int EnabledUsers { get; set; }
    public int DisabledUsers { get; set; }
    public decimal EnabledPercentage { get; set; }
    public int TotalUsageCount { get; set; }
    public int SuccessfulUsageCount { get; set; }
    public int FailedUsageCount { get; set; }
    public decimal SuccessRate { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    public Dictionary<string, int> UsageByShop { get; set; } = new();
    public Dictionary<string, int> UsageByUser { get; set; } = new();
    public List<FeatureFlagUsageEvent> RecentEvents { get; set; } = new();
}

/// <summary>
/// Feature flag usage event
/// </summary>
public class FeatureFlagUsageEvent
{
    public Guid Id { get; set; }
    public FeatureFlag FeatureFlag { get; set; }
    public Guid? ShopId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public bool WasEnabled { get; set; }
    public bool WasSuccessful { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Context { get; set; } = new();
}

/// <summary>
/// Sales calculation precision settings
/// </summary>
public class SalesCalculationPrecisionSettings
{
    public int PricePrecision { get; set; } = 2;
    public int WeightPrecision { get; set; } = 3;
    public int QuantityPrecision { get; set; } = 0;
    public int TaxPrecision { get; set; } = 2;
    public int DiscountPrecision { get; set; } = 2;
    public int TotalPrecision { get; set; } = 2;
    public RoundingMode RoundingMode { get; set; } = RoundingMode.MidpointToEven;
    public bool UseHighPrecisionCalculations { get; set; } = false;
    public int InternalCalculationPrecision { get; set; } = 6;
}

/// <summary>
/// Rounding modes for calculations
/// </summary>
public enum RoundingMode
{
    /// <summary>
    /// Round to nearest, ties to even
    /// </summary>
    MidpointToEven,
    
    /// <summary>
    /// Round to nearest, ties away from zero
    /// </summary>
    MidpointAwayFromZero,
    
    /// <summary>
    /// Always round up
    /// </summary>
    Up,
    
    /// <summary>
    /// Always round down
    /// </summary>
    Down,
    
    /// <summary>
    /// Round towards zero
    /// </summary>
    TowardsZero,
    
    /// <summary>
    /// Round away from zero
    /// </summary>
    AwayFromZero
}

/// <summary>
/// Enhanced performance tuning settings for sales operations
/// </summary>
public class SalesPerformanceSettings
{
    public int MaxConcurrentSales { get; set; } = 10;
    public int CalculationTimeoutMs { get; set; } = 100;
    public int ValidationTimeoutMs { get; set; } = 50;
    public int DatabaseQueryTimeoutMs { get; set; } = 200;
    public bool EnableCalculationCaching { get; set; } = true;
    public int CalculationCacheExpiryMinutes { get; set; } = 5;
    public bool EnableStockValidationCaching { get; set; } = true;
    public int StockValidationCacheExpiryMinutes { get; set; } = 2;
    public bool EnableDiscountCaching { get; set; } = true;
    public int DiscountCacheExpiryMinutes { get; set; } = 10;
    public bool EnableBatchProcessing { get; set; } = true;
    public int BatchSize { get; set; } = 50;
    public bool EnableAsyncProcessing { get; set; } = true;
    public int MaxAsyncOperations { get; set; } = 5;
    public bool EnableMemoryOptimization { get; set; } = true;
    public int MemoryThresholdMB { get; set; } = 100;
    public bool EnablePerformanceMonitoring { get; set; } = true;
    public int PerformanceLogIntervalMinutes { get; set; } = 15;
}

/// <summary>
/// Feature flag settings for gradual rollout
/// </summary>
public class FeatureFlagSettings
{
    public bool EnhancedRealTimeCalculations { get; set; } = true;
    public bool AdvancedDiscountProcessing { get; set; } = true;
    public bool ImprovedWeightBasedPricing { get; set; } = true;
    public bool EnhancedStockValidation { get; set; } = true;
    public bool AdvancedPaymentProcessing { get; set; } = true;
    public bool RealTimeInventoryUpdates { get; set; } = true;
    public bool ComprehensiveAuditLogging { get; set; } = true;
    public bool PerformanceOptimizations { get; set; } = true;
    public bool AdvancedErrorHandling { get; set; } = true;
    public bool EnhancedInputValidation { get; set; } = true;
    public bool MultiCurrencySupport { get; set; } = false;
    public bool AdvancedTaxCalculations { get; set; } = false;
    public bool BulkOperationsSupport { get; set; } = false;
    public bool AdvancedReporting { get; set; } = false;
    public bool AIPoweredRecommendations { get; set; } = false;
}

/// <summary>
/// Shop-specific sales configuration
/// </summary>
public class ShopSalesConfiguration
{
    public Guid ShopId { get; set; }
    public SalesCalculationPrecisionSettings CalculationPrecision { get; set; } = new();
    public SalesPerformanceSettings Performance { get; set; } = new();
    public ShopPricingSettings Pricing { get; set; } = new();
    public ShopTaxSettings Tax { get; set; } = new();
    public Dictionary<FeatureFlag, bool> FeatureFlags { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public Guid LastUpdatedBy { get; set; }
}

/// <summary>
/// User-specific calculation preferences
/// </summary>
public class UserCalculationPreferences
{
    public Guid UserId { get; set; }
    public int DisplayPrecision { get; set; } = 2;
    public bool ShowCalculationBreakdown { get; set; } = true;
    public bool ShowTaxDetails { get; set; } = true;
    public bool ShowDiscountDetails { get; set; } = true;
    public bool EnableRealTimeUpdates { get; set; } = true;
    public bool EnableSoundFeedback { get; set; } = true;
    public bool EnableHapticFeedback { get; set; } = true;
    public string PreferredCurrencyFormat { get; set; } = "C2";
    public string PreferredNumberFormat { get; set; } = "N2";
    public bool UseCompactNumbers { get; set; } = false;
    public Dictionary<string, object> CustomPreferences { get; set; } = new();
}