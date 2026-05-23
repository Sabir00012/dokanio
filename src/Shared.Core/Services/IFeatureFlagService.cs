using Shared.Core.DTOs;
using Shared.Core.Enums;

namespace Shared.Core.Services;

/// <summary>
/// Service interface for managing feature flags
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Checks if a feature flag is enabled globally
    /// </summary>
    /// <param name="featureFlag">Feature flag to check</param>
    /// <returns>True if enabled, false otherwise</returns>
    Task<bool> IsFeatureEnabledAsync(FeatureFlag featureFlag);
    
    /// <summary>
    /// Checks if a feature flag is enabled for a specific shop
    /// </summary>
    /// <param name="featureFlag">Feature flag to check</param>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>True if enabled, false otherwise</returns>
    Task<bool> IsFeatureEnabledForShopAsync(FeatureFlag featureFlag, Guid shopId);
    
    /// <summary>
    /// Checks if a feature flag is enabled for a specific user
    /// </summary>
    /// <param name="featureFlag">Feature flag to check</param>
    /// <param name="userId">User identifier</param>
    /// <returns>True if enabled, false otherwise</returns>
    Task<bool> IsFeatureEnabledForUserAsync(FeatureFlag featureFlag, Guid userId);
    
    /// <summary>
    /// Enables a feature flag globally
    /// </summary>
    /// <param name="featureFlag">Feature flag to enable</param>
    /// <param name="rolloutPercentage">Percentage of users to enable for (0-100)</param>
    /// <returns>Task</returns>
    Task EnableFeatureAsync(FeatureFlag featureFlag, int rolloutPercentage = 100);
    
    /// <summary>
    /// Disables a feature flag globally
    /// </summary>
    /// <param name="featureFlag">Feature flag to disable</param>
    /// <returns>Task</returns>
    Task DisableFeatureAsync(FeatureFlag featureFlag);
    
    /// <summary>
    /// Enables a feature flag for a specific shop
    /// </summary>
    /// <param name="featureFlag">Feature flag to enable</param>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>Task</returns>
    Task EnableFeatureForShopAsync(FeatureFlag featureFlag, Guid shopId);
    
    /// <summary>
    /// Disables a feature flag for a specific shop
    /// </summary>
    /// <param name="featureFlag">Feature flag to disable</param>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>Task</returns>
    Task DisableFeatureForShopAsync(FeatureFlag featureFlag, Guid shopId);
    
    /// <summary>
    /// Enables a feature flag for a specific user
    /// </summary>
    /// <param name="featureFlag">Feature flag to enable</param>
    /// <param name="userId">User identifier</param>
    /// <returns>Task</returns>
    Task EnableFeatureForUserAsync(FeatureFlag featureFlag, Guid userId);
    
    /// <summary>
    /// Disables a feature flag for a specific user
    /// </summary>
    /// <param name="featureFlag">Feature flag to disable</param>
    /// <param name="userId">User identifier</param>
    /// <returns>Task</returns>
    Task DisableFeatureForUserAsync(FeatureFlag featureFlag, Guid userId);
    
    /// <summary>
    /// Gets all feature flags and their current status
    /// </summary>
    /// <returns>List of feature flag configurations</returns>
    Task<IEnumerable<FeatureFlagConfiguration>> GetAllFeatureFlagsAsync();
    
    /// <summary>
    /// Gets feature flags for a specific shop
    /// </summary>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>List of feature flag configurations for the shop</returns>
    Task<IEnumerable<FeatureFlagConfiguration>> GetShopFeatureFlagsAsync(Guid shopId);
    
    /// <summary>
    /// Gets feature flags for a specific user
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <returns>List of feature flag configurations for the user</returns>
    Task<IEnumerable<FeatureFlagConfiguration>> GetUserFeatureFlagsAsync(Guid userId);
    
    /// <summary>
    /// Updates rollout percentage for a feature flag
    /// </summary>
    /// <param name="featureFlag">Feature flag to update</param>
    /// <param name="rolloutPercentage">New rollout percentage (0-100)</param>
    /// <returns>Task</returns>
    Task UpdateRolloutPercentageAsync(FeatureFlag featureFlag, int rolloutPercentage);
    
    /// <summary>
    /// Gets feature flag usage analytics
    /// </summary>
    /// <param name="featureFlag">Feature flag to analyze</param>
    /// <param name="fromDate">Start date for analytics</param>
    /// <param name="toDate">End date for analytics</param>
    /// <returns>Feature flag analytics</returns>
    Task<FeatureFlagAnalytics> GetFeatureFlagAnalyticsAsync(FeatureFlag featureFlag, DateTime fromDate, DateTime toDate);
    
    /// <summary>
    /// Initializes default feature flag configurations
    /// </summary>
    /// <returns>Task</returns>
    Task InitializeDefaultFeatureFlagsAsync();
}