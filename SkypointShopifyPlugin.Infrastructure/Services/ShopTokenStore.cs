using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Infrastructure.Data;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class ShopTokenStoreOptions
    {
        public string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data");
    }

    /// <summary>
    /// Database-backed Shopify OAuth token store.
    /// Provides stateless token persistence across server instances.
    /// Includes a one-time migration for legacy file-based tokens.
    /// </summary>
    public class ShopTokenStore : IShopTokenStore
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<ShopTokenStore> _logger;
        private readonly string _dataDirectory;

        public ShopTokenStore(
            IServiceScopeFactory scopeFactory,
            IEncryptionService encryptionService,
            IOptions<ShopTokenStoreOptions> options,
            ILogger<ShopTokenStore> logger)
        {
            _scopeFactory = scopeFactory;
            _encryptionService = encryptionService;
            _logger = logger;
            _dataDirectory = options.Value.DataDirectory;

            // Perform one-time migration from file-based storage on startup
            MigrateLegacyFiles();
        }

        public void SaveToken(string shopDomain, string accessToken)
        {
            shopDomain = Normalize(shopDomain);
            var (encToken, iv) = _encryptionService.Encrypt(accessToken);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.ShopifyTokens.FirstOrDefault(t => t.ShopDomain == shopDomain);
            if (entity == null)
            {
                entity = new ShopifyTokenEntity
                {
                    ShopDomain = shopDomain,
                    EncryptedToken = encToken,
                    Iv = iv,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.ShopifyTokens.Add(entity);
            }
            else
            {
                entity.EncryptedToken = encToken;
                entity.Iv = iv;
                entity.UpdatedAt = DateTime.UtcNow;
                db.ShopifyTokens.Update(entity);
            }

            db.SaveChanges();
            _logger.LogInformation("Shopify token saved in database for shop: {Shop}", shopDomain);
        }

        public string? GetToken(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.ShopifyTokens.FirstOrDefault(t => t.ShopDomain == shopDomain);
            if (entity == null)
            {
                return null;
            }

            try
            {
                return _encryptionService.Decrypt(entity.EncryptedToken, entity.Iv);
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

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            return db.ShopifyTokens.Any(t => t.ShopDomain == shopDomain);
        }

        public void RemoveToken(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.ShopifyTokens.FirstOrDefault(t => t.ShopDomain == shopDomain);
            if (entity != null)
            {
                db.ShopifyTokens.Remove(entity);
                db.SaveChanges();
                _logger.LogInformation("Shopify token removed from database for shop: {Shop}", shopDomain);
            }
        }

        public IReadOnlyList<string> GetAllShops()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            return db.ShopifyTokens.Select(t => t.ShopDomain).ToList();
        }

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');

        private void MigrateLegacyFiles()
        {
            try
            {
                var legacyEncryptedFile = Path.Combine(_dataDirectory, "shopify_tokens.json");
                var legacyPlainTextFile = Path.Combine(_dataDirectory, "shop_tokens.json");

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

                // 1. Migrate plain text legacy tokens if database is empty
                if (File.Exists(legacyPlainTextFile))
                {
                    _logger.LogInformation("Found legacy plain-text token file: {Path}. Starting migration.", legacyPlainTextFile);
                    var json = File.ReadAllText(legacyPlainTextFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        foreach (var (shop, token) in dict)
                        {
                            var normalized = Normalize(shop);
                            if (string.IsNullOrWhiteSpace(token)) continue;

                            if (!db.ShopifyTokens.Any(t => t.ShopDomain == normalized))
                            {
                                var (encToken, iv) = _encryptionService.Encrypt(token);
                                db.ShopifyTokens.Add(new ShopifyTokenEntity
                                {
                                    ShopDomain = normalized,
                                    EncryptedToken = encToken,
                                    Iv = iv
                                });
                            }
                        }
                        db.SaveChanges();
                    }
                    File.Move(legacyPlainTextFile, legacyPlainTextFile + ".migrated", overwrite: true);
                    _logger.LogInformation("Legacy plain-text tokens migrated successfully.");
                }

                // 2. Migrate encrypted legacy tokens if database is empty
                if (File.Exists(legacyEncryptedFile))
                {
                    _logger.LogInformation("Found legacy encrypted token file: {Path}. Starting migration.", legacyEncryptedFile);
                    var json = File.ReadAllText(legacyEncryptedFile);
                    
                    // We need a temporary local key decryption for the file if it was encrypted with the old shop key file
                    var keyFile = Path.Combine(_dataDirectory, "skypoint_key.bin");
                    byte[]? oldKey = null;
                    if (File.Exists(keyFile))
                    {
                        oldKey = File.ReadAllBytes(keyFile);
                    }

                    if (oldKey != null && oldKey.Length == 32)
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, LegacyTokenRecord>>(json);
                        if (dict != null)
                        {
                            using var aes = new System.Security.Cryptography.AesGcm(oldKey, 16);
                            foreach (var (shop, record) in dict)
                            {
                                var normalized = Normalize(shop);
                                try
                                {
                                    // Decrypt using old key
                                    var combined = Convert.FromBase64String(record.EncryptedToken);
                                    var ivBytes = Convert.FromBase64String(record.IV);
                                    var ciphertext = combined[..^16];
                                    var tag = combined[^16..];
                                    var plaintextBytes = new byte[ciphertext.Length];
                                    aes.Decrypt(ivBytes, ciphertext, tag, plaintextBytes);
                                    var token = System.Text.Encoding.UTF8.GetString(plaintextBytes);

                                    // Save via new database context
                                    if (!db.ShopifyTokens.Any(t => t.ShopDomain == normalized))
                                    {
                                        var (newEncToken, newIv) = _encryptionService.Encrypt(token);
                                        db.ShopifyTokens.Add(new ShopifyTokenEntity
                                        {
                                            ShopDomain = normalized,
                                            EncryptedToken = newEncToken,
                                            Iv = newIv
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to migrate token for {Shop} during legacy decryption.", shop);
                                }
                            }
                            db.SaveChanges();
                        }
                    }
                    File.Move(legacyEncryptedFile, legacyEncryptedFile + ".migrated", overwrite: true);
                    _logger.LogInformation("Legacy encrypted tokens migrated successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating legacy shop tokens to database.");
            }
        }

        private record LegacyTokenRecord(string EncryptedToken, string IV);
    }
}
