using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class ShopTokenStoreOptions
    {
        public string DataDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "data");
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
        private readonly HttpClient _httpClient;
        private readonly ShopifySettings _shopifySettings;

        public ShopTokenStore(
            IEncryptionService encryptionService,
            IOptions<ShopTokenStoreOptions> options,
            ILogger<ShopTokenStore> logger,
            HttpClient httpClient,
            IOptions<ShopifySettings> shopifySettings)
        {
            _encryptionService = encryptionService;
            _logger = logger;
            _httpClient = httpClient;
            _shopifySettings = shopifySettings.Value;
            
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
            SaveToken(shopDomain, accessToken, null, null);
        }

        public void SaveToken(string shopDomain, string accessToken, string? refreshToken, int? expiresInSeconds)
        {
            shopDomain = Normalize(shopDomain);
            var (encToken, iv) = _encryptionService.Encrypt(accessToken);
            string? encRefreshToken = null;
            string? refreshIv = null;
            DateTime? expiresAt = null;

            if (!string.IsNullOrEmpty(refreshToken))
            {
                var (encRef, rIv) = _encryptionService.Encrypt(refreshToken);
                encRefreshToken = encRef;
                refreshIv = rIv;
            }

            if (expiresInSeconds.HasValue)
            {
                expiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds.Value);
            }

            var tokens = LoadAll();
            tokens[shopDomain] = new TokenRecord(encToken, iv, encRefreshToken, refreshIv, expiresAt);
            SaveAll(tokens);
            _logger.LogInformation("Saved Shopify token for shop: {Shop}. ExpiresAt: {ExpiresAt}", shopDomain, expiresAt);
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
                var decryptedToken = _encryptionService.Decrypt(record.EncryptedToken, record.IV);

                // Check if the token is expiring in less than 5 minutes or already expired
                if (record.ExpiresAt.HasValue && record.ExpiresAt.Value <= DateTime.UtcNow.AddMinutes(5))
                {
                    if (!string.IsNullOrEmpty(record.EncryptedRefreshToken) && !string.IsNullOrEmpty(record.RefreshIV))
                    {
                        var refreshToken = _encryptionService.Decrypt(record.EncryptedRefreshToken, record.RefreshIV);
                        _logger.LogInformation("Shopify token for {Shop} is expiring/expired. Attempting synchronous refresh...", shopDomain);
                        
                        var refreshedToken = RefreshShopifyToken(shopDomain, refreshToken);
                        if (refreshedToken != null)
                        {
                            return refreshedToken;
                        }
                    }
                }

                return decryptedToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt/retrieve Shopify token for shop: {Shop}", shopDomain);
                return null;
            }
        }

        private string? RefreshShopifyToken(string shop, string refreshToken)
        {
            try
            {
                var tokenUrl = $"https://{shop}/admin/oauth/access_token";
                var requestBody = new
                {
                    client_id = _shopifySettings.ClientId,
                    client_secret = _shopifySettings.ClientSecret,
                    grant_type = "refresh_token",
                    refresh_token = refreshToken
                };
                
                var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };

                using var response = _httpClient.Send(request);
                if (!response.IsSuccessStatusCode)
                {
                    using var errReader = new StreamReader(response.Content.ReadAsStream());
                    var errBody = errReader.ReadToEnd();
                    _logger.LogError("Token refresh request failed for {Shop}: {Status} - {Body}", shop, response.StatusCode, errBody);
                    return null;
                }

                using var reader = new StreamReader(response.Content.ReadAsStream());
                var responseBody = reader.ReadToEnd();
                
                var tokenResponse = JsonSerializer.Deserialize<ShopifyTokenResponse>(responseBody);
                if (tokenResponse?.access_token != null)
                {
                    _logger.LogInformation("Successfully refreshed token for shop: {Shop}", shop);
                    
                    // Save the new token details (SaveToken handles locking and load/save file internally)
                    SaveToken(shop, tokenResponse.access_token, tokenResponse.refresh_token, tokenResponse.expires_in);
                    return tokenResponse.access_token;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during synchronous Shopify token refresh for shop {Shop}", shop);
            }

            return null;
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

        private record TokenRecord(
            string EncryptedToken,
            string IV,
            string? EncryptedRefreshToken = null,
            string? RefreshIV = null,
            DateTime? ExpiresAt = null);
    }
}
