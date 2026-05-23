namespace Shared.Core.Services;

/// <summary>
/// Service interface for initializing configuration settings
/// </summary>
public interface IConfigurationInitializationService
{
    /// <summary>
    /// Initializes all default configuration settings
    /// </summary>
    /// <returns>Task</returns>
    Task InitializeDefaultConfigurationsAsync();
    
    /// <summary>
    /// Initializes configuration for a new shop
    /// </summary>
    /// <param name="shopId">Shop identifier</param>
    /// <returns>Task</returns>
    Task InitializeShopConfigurationAsync(Guid shopId);
    
    /// <summary>
    /// Initializes configuration for a new user
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <returns>Task</returns>
    Task InitializeUserConfigurationAsync(Guid userId);
    
    /// <summary>
    /// Validates all configuration settings
    /// </summary>
    /// <returns>True if all configurations are valid</returns>
    Task<bool> ValidateAllConfigurationsAsync();
    
    /// <summary>
    /// Migrates configuration settings to new version
    /// </summary>
    /// <param name="fromVersion">Source version</param>
    /// <param name="toVersion">Target version</param>
    /// <returns>Task</returns>
    Task MigrateConfigurationAsync(string fromVersion, string toVersion);
}