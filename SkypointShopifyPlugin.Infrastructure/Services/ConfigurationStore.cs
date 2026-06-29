using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.DTOs.Configuration;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// File-based configuration store
    /// Saves configuration to data/app_config.json
    /// </summary>
    public class ConfigurationStore : IConfigurationStore
    {
        private readonly string _configFilePath;
        private readonly ILogger<ConfigurationStore> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public ConfigurationStore(ILogger<ConfigurationStore> logger)
        {
            _logger = logger;
            _configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "app_config.json");
            
            // Ensure data directory exists
            var dataDir = Path.GetDirectoryName(_configFilePath);
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir!);
            }

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task SaveConfigurationAsync(AppConfiguration config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, _jsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
                _logger.LogInformation("Configuration saved to {Path}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to {Path}", _configFilePath);
                throw;
            }
        }

        public async Task<AppConfiguration?> LoadConfigurationAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    _logger.LogInformation("Configuration file not found at {Path}", _configFilePath);
                    return null;
                }

                var json = await File.ReadAllTextAsync(_configFilePath);
                var config = JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions);
                _logger.LogInformation("Configuration loaded from {Path}", _configFilePath);
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from {Path}", _configFilePath);
                return null;
            }
        }

        public bool ConfigurationExists()
        {
            return File.Exists(_configFilePath);
        }
    }
}
