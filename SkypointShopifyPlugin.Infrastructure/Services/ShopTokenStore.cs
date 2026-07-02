using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class ShopTokenStoreOptions
    {
        public string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data");
    }

    /// <summary>
    /// File-based Shopify OAuth token store.
    /// Provides token persistence across server instances via shopify_tokens.json.
    /// Includes safe automatic restoration of previously migrated .migrated backups.
    /// </summary>
    public class ShopTokenStore : IShopTokenStore
    {
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<ShopTokenStore> _logger;
        private readonly string _tokensFile;
        private readonly object _lock = new();

        public ShopTokenStore(
            IEncryptionService encryptionService,
            IOptions<ShopTokenStoreOptions> options,
            ILogger<ShopTokenStore> logger)
        {
            _encryptionService = encryptionService;
            _logger = logger;
            
            var dataDir = options.Value.DataDirectory;
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            
            _tokensFile = Path.Combine(dataDir, "shopify_tokens.json");
            var migratedFile = _tokensFile + ".migrated";
            
            // Safe fallback: rename migrated file back if it exists and the active file does not
            if (!File.Exists(_tokensFile) && File.Exists(migratedFile))
            {
                try
                {
                    File.Move(migratedFile, _tokensFile);
                    _logger.LogInformation("Restored legacy shopify_tokens.json from migrated file.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore migrated shopify_tokens.json file");
                }
            }
        }

        private Dictionary<string, TokenRecord> LoadAll()
        {
            lock (_lock)
            {
                if (!File.Exists(_tokensFile))
                {
                    return new Dictionary<string, TokenRecord>(StringComparer.OrdinalIgnoreCase);
                }

                try
                {
                    var json = File.ReadAllText(_tokensFile);
                    return JsonSerializer.Deserialize<Dictionary<string, TokenRecord>>(json) 
                           ?? new Dictionary<string, TokenRecord>(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load shopify tokens file.");
                    return new Dictionary<string, TokenRecord>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        private void SaveAll(Dictionary<string, TokenRecord> tokens)
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_tokensFile, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save shopify tokens file.");
                }
            }
        }

        public void SaveToken(string shopDomain, string accessToken)
        {
            shopDomain = Normalize(shopDomain);
            var (encToken, iv) = _encryptionService.Encrypt(accessToken);

            var tokens = LoadAll();
            tokens[shopDomain] = new TokenRecord(encToken, iv);
            SaveAll(tokens);
            _logger.LogInformation("Saved Shopify token for shop: {Shop}", shopDomain);
        }

        public string? GetToken(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            var tokens = LoadAll();

            if (!tokens.TryGetValue(shopDomain, out var record))
            {
                return null;
            }

            try
            {
                return _encryptionService.Decrypt(record.EncryptedToken, record.IV);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt Shopify token for shop: {Shop}", shopDomain);
                return null;
            }
        }

        public bool HasToken(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            var tokens = LoadAll();
            return tokens.ContainsKey(shopDomain);
        }

        public void RemoveToken(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            var tokens = LoadAll();
            if (tokens.Remove(shopDomain))
            {
                SaveAll(tokens);
                _logger.LogInformation("Removed Shopify token for shop: {Shop}", shopDomain);
            }
        }

        public IReadOnlyList<string> GetAllShops()
        {
            var tokens = LoadAll();
            return new List<string>(tokens.Keys);
        }

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');

        private record TokenRecord(string EncryptedToken, string IV);
    }
}
