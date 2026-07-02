using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.DTOs.Configuration;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// File-based configuration store.
    /// Saves and loads application configuration from app_config.json in the data directory.
    /// Includes safe automatic restoration of previously migrated .migrated backups.
    /// </summary>
    public class ConfigurationStore : IConfigurationStore
    {
        private readonly ILogger<ConfigurationStore> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _configPath;
        private readonly object _lock = new();

        public ConfigurationStore(ILogger<ConfigurationStore> logger)
        {
            _logger = logger;
            var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }
            _configPath = Path.Combine(dataDirectory, "app_config.json");
            var migratedConfigPath = _configPath + ".migrated";

            // Safely restore previously migrated configuration if active configuration is missing
            if (!File.Exists(_configPath) && File.Exists(migratedConfigPath))
            {
                try
                {
                    File.Move(migratedConfigPath, _configPath);
                    _logger.LogInformation("Restored legacy app_config.json from migrated file.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore migrated configuration file: {Path}", migratedConfigPath);
                }
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
                lock (_lock)
                {
                    File.WriteAllText(_configPath, json);
                }
                _logger.LogInformation("Configuration saved successfully to {Path}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to {Path}", _configPath);
                throw;
            }
            await Task.CompletedTask;
        }

        public async Task<AppConfiguration?> LoadConfigurationAsync()
        {
            try
            {
                lock (_lock)
                {
                    if (!File.Exists(_configPath))
                    {
                        _logger.LogInformation("Configuration not found at {Path}.", _configPath);
                        return null;
                    }
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions);
                    _logger.LogInformation("Configuration loaded successfully from {Path}", _configPath);
                    return config;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from {Path}", _configPath);
                return null;
            }
        }

        public bool ConfigurationExists()
        {
            lock (_lock)
            {
                return File.Exists(_configPath);
            }
        }
    }
}
