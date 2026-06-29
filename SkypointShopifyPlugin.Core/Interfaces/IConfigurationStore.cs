using SkypointShopifyPlugin.Core.DTOs.Configuration;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    /// <summary>
    /// Interface for storing and retrieving application configuration
    /// Configuration is persisted to disk for centralized setup
    /// </summary>
    public interface IConfigurationStore
    {
        /// <summary>
        /// Save application configuration
        /// </summary>
        Task SaveConfigurationAsync(AppConfiguration config);

        /// <summary>
        /// Load application configuration
        /// </summary>
        Task<AppConfiguration?> LoadConfigurationAsync();

        /// <summary>
        /// Check if configuration exists
        /// </summary>
        bool ConfigurationExists();
    }
}
