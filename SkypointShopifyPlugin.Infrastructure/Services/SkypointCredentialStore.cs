using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class SkypointCredentialStoreOptions
    {
        public string DataDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "data");
    }

    /// <summary>
    /// File-based Skypoint credential store.
    /// Provides credential persistence across server instances via skypoint_credentials.json.
    /// Includes safe automatic restoration of previously migrated .migrated backups.
    /// </summary>
    public class SkypointCredentialStore : ISkypointCredentialStore
    {
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<SkypointCredentialStore> _logger;
        private readonly string _credsFile;
        private readonly object _lock = new();

        public SkypointCredentialStore(
            IEncryptionService encryptionService,
            IOptions<SkypointCredentialStoreOptions> options,
            ILogger<SkypointCredentialStore> logger)
        {
            _encryptionService = encryptionService;
            _logger = logger;
            
            var dataDir = options.Value.DataDirectory;
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            
            _credsFile = Path.Combine(dataDir, "skypoint_credentials.json");
            var migratedFile = _credsFile + ".migrated";
            
            // Safe fallback: rename migrated file back if it exists and the active file does not
            if (!File.Exists(_credsFile) && File.Exists(migratedFile))
            {
                try
                {
                    File.Move(migratedFile, _credsFile);
                    _logger.LogInformation("Restored legacy skypoint_credentials.json from migrated file.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore migrated skypoint_credentials.json file");
                }
            }
        }

        private Dictionary<string, CredRecord> LoadAll()
        {
            lock (_lock)
            {
                if (!File.Exists(_credsFile))
                {
                    return new Dictionary<string, CredRecord>(StringComparer.OrdinalIgnoreCase);
                }

                try
                {
                    var json = File.ReadAllText(_credsFile);
                    return JsonSerializer.Deserialize<Dictionary<string, CredRecord>>(json) 
                           ?? new Dictionary<string, CredRecord>(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load skypoint credentials file.");
                    return new Dictionary<string, CredRecord>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        private void SaveAll(Dictionary<string, CredRecord> creds)
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(creds, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_credsFile, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save skypoint credentials file.");
                }
            }
        }

        public void Save(string shopDomain, string username, string password)
        {
            shopDomain = Normalize(shopDomain);
            var (encPassword, iv) = _encryptionService.Encrypt(password);

            var creds = LoadAll();
            creds[shopDomain] = new CredRecord(username, encPassword, iv);
            SaveAll(creds);
            _logger.LogInformation("Saved Skypoint credentials for shop: {Shop}", shopDomain);
        }

        public (string Username, string Password)? Get(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            var creds = LoadAll();

            if (!creds.TryGetValue(shopDomain, out var record))
            {
                return null;
            }

            try
            {
                var password = _encryptionService.Decrypt(record.EncryptedPassword, record.IV);
                return (record.Username, password);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt Skypoint credentials for shop: {Shop}", shopDomain);
                return null;
            }
        }

        public IReadOnlyList<string> GetAllShops()
        {
            var creds = LoadAll();
            return new List<string>(creds.Keys);
        }

        public void Remove(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            var creds = LoadAll();
            if (creds.Remove(shopDomain))
            {
                SaveAll(creds);
                _logger.LogInformation("Removed Skypoint credentials for shop: {Shop}", shopDomain);
            }
        }

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');

        private record CredRecord(string Username, string EncryptedPassword, string IV);
    }
}
