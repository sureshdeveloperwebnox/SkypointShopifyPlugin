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
    public class SkypointCredentialStoreOptions
    {
        public string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data");
    }

    /// <summary>
    /// Database-backed Skypoint credential store.
    /// Provides stateless credential management across server instances.
    /// Includes a one-time migration for legacy file-based credentials.
    /// </summary>
    public class SkypointCredentialStore : ISkypointCredentialStore
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<SkypointCredentialStore> _logger;
        private readonly string _dataDirectory;

        public SkypointCredentialStore(
            IServiceScopeFactory scopeFactory,
            IEncryptionService encryptionService,
            IOptions<SkypointCredentialStoreOptions> options,
            ILogger<SkypointCredentialStore> logger)
        {
            _scopeFactory = scopeFactory;
            _encryptionService = encryptionService;
            _logger = logger;
            _dataDirectory = options.Value.DataDirectory;

            // Perform one-time migration on startup
            MigrateLegacyFiles();
        }

        public void Save(string shopDomain, string username, string password)
        {
            shopDomain = Normalize(shopDomain);
            var (encPassword, iv) = _encryptionService.Encrypt(password);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.SkypointCredentials.FirstOrDefault(c => c.ShopDomain == shopDomain);
            if (entity == null)
            {
                entity = new SkypointCredentialEntity
                {
                    ShopDomain = shopDomain,
                    Username = username,
                    EncryptedPassword = encPassword,
                    Iv = iv,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.SkypointCredentials.Add(entity);
            }
            else
            {
                entity.Username = username;
                entity.EncryptedPassword = encPassword;
                entity.Iv = iv;
                entity.UpdatedAt = DateTime.UtcNow;
                db.SkypointCredentials.Update(entity);
            }

            db.SaveChanges();
            _logger.LogInformation("Skypoint credentials saved in database for shop: {Shop}", shopDomain);
        }

        public (string Username, string Password)? Get(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.SkypointCredentials.FirstOrDefault(c => c.ShopDomain == shopDomain);
            if (entity == null)
            {
                return null;
            }

            try
            {
                var password = _encryptionService.Decrypt(entity.EncryptedPassword, entity.Iv);
                return (entity.Username, password);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt Skypoint credentials for shop: {Shop}", shopDomain);
                return null;
            }
        }

        public IReadOnlyList<string> GetAllShops()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            return db.SkypointCredentials.Select(c => c.ShopDomain).ToList();
        }

        public void Remove(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.SkypointCredentials.FirstOrDefault(c => c.ShopDomain == shopDomain);
            if (entity != null)
            {
                db.SkypointCredentials.Remove(entity);
                db.SaveChanges();
                _logger.LogInformation("Skypoint credentials removed from database for shop: {Shop}", shopDomain);
            }
        }

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');

        private void MigrateLegacyFiles()
        {
            try
            {
                var legacyCredFile = Path.Combine(_dataDirectory, "skypoint_credentials.json");

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

                if (File.Exists(legacyCredFile))
                {
                    _logger.LogInformation("Found legacy credentials file: {Path}. Starting migration.", legacyCredFile);
                    var json = File.ReadAllText(legacyCredFile);

                    var keyFile = Path.Combine(_dataDirectory, "skypoint_key.bin");
                    byte[]? oldKey = null;
                    if (File.Exists(keyFile))
                    {
                        oldKey = File.ReadAllBytes(keyFile);
                    }

                    if (oldKey != null && oldKey.Length == 32)
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, LegacyCredRecord>>(json);
                        if (dict != null)
                        {
                            using var aes = new System.Security.Cryptography.AesGcm(oldKey, 16);
                            foreach (var (shop, record) in dict)
                            {
                                var normalized = Normalize(shop);
                                try
                                {
                                    // Decrypt legacy record
                                    var combined = Convert.FromBase64String(record.EncryptedPassword);
                                    var ivBytes = Convert.FromBase64String(record.IV);
                                    var ciphertext = combined[..^16];
                                    var tag = combined[^16..];
                                    var plaintextBytes = new byte[ciphertext.Length];
                                    aes.Decrypt(ivBytes, ciphertext, tag, plaintextBytes);
                                    var password = System.Text.Encoding.UTF8.GetString(plaintextBytes);

                                    // Save to database
                                    if (!db.SkypointCredentials.Any(c => c.ShopDomain == normalized))
                                    {
                                        var (newEncPassword, newIv) = _encryptionService.Encrypt(password);
                                        db.SkypointCredentials.Add(new SkypointCredentialEntity
                                        {
                                            ShopDomain = normalized,
                                            Username = record.Username,
                                            EncryptedPassword = newEncPassword,
                                            Iv = newIv
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to migrate credentials for {Shop} during decryption.", shop);
                                }
                            }
                            db.SaveChanges();
                        }
                    }
                    File.Move(legacyCredFile, legacyCredFile + ".migrated", overwrite: true);
                    _logger.LogInformation("Legacy credentials migrated successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating legacy Skypoint credentials to database.");
            }
        }

        private record LegacyCredRecord(string Username, string EncryptedPassword, string IV);
    }
}
