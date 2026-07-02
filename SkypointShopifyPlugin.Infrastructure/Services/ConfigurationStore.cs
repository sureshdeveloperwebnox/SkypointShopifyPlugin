using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.DTOs.Configuration;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Infrastructure.Data;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Database-backed configuration store.
    /// Saves application configuration to the database rather than a local JSON file.
    /// Includes a one-time migration for legacy app_config.json files.
    /// </summary>
    public class ConfigurationStore : IConfigurationStore
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ConfigurationStore> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _legacyConfigPath;

        public ConfigurationStore(
            IServiceScopeFactory scopeFactory,
            ILogger<ConfigurationStore> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _legacyConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "app_config.json");
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Migrate legacy config file to database on startup
            MigrateLegacyFile();
        }

        public async Task SaveConfigurationAsync(AppConfiguration config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, _jsonOptions);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

                var entity = db.AppConfigurations.FirstOrDefault(c => c.Id == 1);
                if (entity == null)
                {
                    entity = new AppConfigurationEntity
                    {
                        Id = 1,
                        ConfigurationJson = json,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.AppConfigurations.Add(entity);
                }
                else
                {
                    entity.ConfigurationJson = json;
                    entity.UpdatedAt = DateTime.UtcNow;
                    db.AppConfigurations.Update(entity);
                }

                await db.SaveChangesAsync();
                _logger.LogInformation("Configuration saved successfully to the database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to the database");
                throw;
            }
        }

        public async Task<AppConfiguration?> LoadConfigurationAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

                var entity = db.AppConfigurations.FirstOrDefault(c => c.Id == 1);
                if (entity == null)
                {
                    _logger.LogInformation("Configuration not found in database.");
                    return null;
                }

                var config = JsonSerializer.Deserialize<AppConfiguration>(entity.ConfigurationJson, _jsonOptions);
                _logger.LogInformation("Configuration loaded successfully from the database");
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from the database");
                return null;
            }
        }

        public bool ConfigurationExists()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            return db.AppConfigurations.Any(c => c.Id == 1);
        }

        private void MigrateLegacyFile()
        {
            try
            {
                if (File.Exists(_legacyConfigPath))
                {
                    _logger.LogInformation("Found legacy configuration file: {Path}. Starting migration to database.", _legacyConfigPath);
                    var json = File.ReadAllText(_legacyConfigPath);

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

                    var entity = db.AppConfigurations.FirstOrDefault(c => c.Id == 1);
                    if (entity == null)
                    {
                        db.AppConfigurations.Add(new AppConfigurationEntity
                        {
                            Id = 1,
                            ConfigurationJson = json,
                            UpdatedAt = DateTime.UtcNow
                        });
                        db.SaveChanges();
                        _logger.LogInformation("Legacy configuration successfully migrated to database.");
                    }

                    File.Move(_legacyConfigPath, _legacyConfigPath + ".migrated", overwrite: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating legacy configuration file to database.");
            }
        }
    }
}
