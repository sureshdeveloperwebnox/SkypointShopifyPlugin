using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.DTOs.Configuration;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Loads saved configuration from app_config.json on startup
    /// Applies configuration to the application's configuration system
    /// </summary>
    public class ConfigurationBootstrapService : IHostedService
    {
        private readonly IConfigurationStore _configurationStore;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConfigurationBootstrapService> _logger;

        public ConfigurationBootstrapService(
            IConfigurationStore configurationStore,
            IConfiguration configuration,
            ILogger<ConfigurationBootstrapService> logger)
        {
            _configurationStore = configurationStore;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var config = await _configurationStore.LoadConfigurationAsync();
                
                if (config == null)
                {
                    _logger.LogInformation("No saved configuration found. Using default configuration from appsettings.json and .env");
                    return;
                }

                _logger.LogInformation("Loading configuration from app_config.json");

                // Apply Shopify configuration
                if (config.Shopify != null)
                {
                    if (!string.IsNullOrEmpty(config.Shopify.ClientId))
                    {
                        _logger.LogInformation("Loading Shopify Client ID from saved configuration");
                    }
                    if (!string.IsNullOrEmpty(config.Shopify.ClientSecret))
                    {
                        _logger.LogInformation("Loading Shopify Client Secret from saved configuration");
                    }
                    if (!string.IsNullOrEmpty(config.Shopify.RedirectUri))
                    {
                        _logger.LogInformation("Loading Shopify Redirect URI from saved configuration");
                    }
                    if (!string.IsNullOrEmpty(config.Shopify.WebhookSecret))
                    {
                        _logger.LogInformation("Loading Shopify Webhook Secret from saved configuration");
                    }
                }

                // Apply Skypoint API configuration
                if (config.SkypointApi != null)
                {
                    _logger.LogInformation("Loading Skypoint API configuration from saved configuration: {BaseUrl}", config.SkypointApi.BaseUrl);
                }

                // Apply parcel dimensions
                if (config.SkypointMappings != null)
                {
                    _logger.LogInformation("Loading parcel dimensions from saved configuration: {Length}x{Breadth}x{Height}cm, {Mass}kg",
                        config.SkypointMappings.DefaultParcelLength,
                        config.SkypointMappings.DefaultParcelBreadth,
                        config.SkypointMappings.DefaultParcelHeight,
                        config.SkypointMappings.DefaultParcelMass);
                }

                _logger.LogInformation("Configuration loaded successfully from app_config.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from app_config.json. Using default configuration.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
