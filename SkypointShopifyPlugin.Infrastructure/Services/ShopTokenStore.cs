using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Options — DataDirectory is injected by the WebAPI host (ContentRootPath/data).
    /// Shared with SkypointCredentialStore so all secrets land in the same folder.
    /// </summary>
    public class ShopTokenStoreOptions
    {
        public string DataDirectory { get; set; } =
            Path.Combine(AppContext.BaseDirectory, "data");
    }

    /// <summary>
    /// Shopify OAuth token store.
    ///
    /// Strategy:
    ///   - In-memory ConcurrentDictionary for fast reads on every carrier rate request.
    ///   - AES-GCM encrypted JSON file for persistence across server restarts.
    ///   - Shares the same 256-bit key file (skypoint_key.bin) as SkypointCredentialStore
    ///     — one key manages all server-side secrets.
    ///
    /// Token lifecycle:
    ///   1. OAuth callback completes → SaveToken() → written to disk immediately
    ///   2. Server restarts → Load() reads + decrypts file → memory warmed on startup
    ///   3. Token rejected by Shopify → RemoveToken() → removed from disk
    ///   4. Merchant uninstalls app → RemoveToken() via webhook
    ///
    /// Nothing is ever sent to the browser. The token lives server-side only.
    /// </summary>
    public class ShopTokenStore : IShopTokenStore
    {
        private record TokenRecord(string EncryptedToken, string IV);

        private readonly ConcurrentDictionary<string, string> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, TokenRecord> _store =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly string _tokenFile;
        private readonly string _keyFile;
        private readonly byte[] _encryptionKey;
        private readonly ILogger<ShopTokenStore> _logger;
        private readonly object _writeLock = new();

        public ShopTokenStore(
            IOptions<ShopTokenStoreOptions> options,
            ILogger<ShopTokenStore> logger)
        {
            _logger = logger;

            var dir = options.Value.DataDirectory;
            Directory.CreateDirectory(dir);

            _tokenFile = Path.Combine(dir, "shopify_tokens.json");
            // Reuse the same key file as SkypointCredentialStore
            _keyFile = Path.Combine(dir, "skypoint_key.bin");

            _encryptionKey = LoadOrCreateKey();
            LoadFromDisk();

            // One-time migration: import plain-text tokens from the old shop_tokens.json
            // format so existing shops don't need to reinstall the app.
            MigrateFromLegacyFile(Path.Combine(dir, "shop_tokens.json"));

            _logger.LogInformation(
                "ShopTokenStore loaded {Count} Shopify token(s) from {Path}",
                _cache.Count, _tokenFile);
        }

        // ── IShopTokenStore ──────────────────────────────────────────────────────

        public void SaveToken(string shopDomain, string accessToken)
        {
            shopDomain = Normalize(shopDomain);
            var (enc, iv) = Encrypt(accessToken);
            _store[shopDomain] = new TokenRecord(enc, iv);
            _cache[shopDomain] = accessToken;
            Persist();
            _logger.LogInformation("Shopify token saved for shop: {Shop}", shopDomain);
        }

        public string? GetToken(string shopDomain)
        {
            _cache.TryGetValue(Normalize(shopDomain), out var token);
            return token;
        }

        public bool HasToken(string shopDomain)
            => _cache.ContainsKey(Normalize(shopDomain));

        public void RemoveToken(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            _cache.TryRemove(shopDomain, out _);
            _store.TryRemove(shopDomain, out _);
            Persist();
            _logger.LogInformation("Shopify token removed for shop: {Shop}", shopDomain);
        }

        public IReadOnlyList<string> GetAllShops()
            => _cache.Keys.ToList();

        // ── Key management ───────────────────────────────────────────────────────

        private byte[] LoadOrCreateKey()
        {
            if (File.Exists(_keyFile))
            {
                var existing = File.ReadAllBytes(_keyFile);
                if (existing.Length == 32)
                    return existing;

                _logger.LogWarning(
                    "Encryption key file corrupt or wrong size — regenerating. " +
                    "Existing encrypted tokens will be unreadable and must be re-acquired via OAuth.");
            }

            var key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(_keyFile, key);
            _logger.LogInformation("Created new encryption key at {Path}", _keyFile);
            return key;
        }

        // ── AES-GCM encrypt/decrypt ──────────────────────────────────────────────

        private (string EncryptedBase64, string IvBase64) Encrypt(string plaintext)
        {
            var iv            = RandomNumberGenerator.GetBytes(12);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext    = new byte[plaintextBytes.Length];
            var tag           = new byte[16];

            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Encrypt(iv, plaintextBytes, ciphertext, tag);

            var combined = new byte[ciphertext.Length + tag.Length];
            ciphertext.CopyTo(combined, 0);
            tag.CopyTo(combined, ciphertext.Length);

            return (Convert.ToBase64String(combined), Convert.ToBase64String(iv));
        }

        private string Decrypt(string encryptedBase64, string ivBase64)
        {
            var combined   = Convert.FromBase64String(encryptedBase64);
            var iv         = Convert.FromBase64String(ivBase64);
            var ciphertext = combined[..^16];
            var tag        = combined[^16..];
            var plaintext  = new byte[ciphertext.Length];

            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Decrypt(iv, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }

        // ── Disk I/O ─────────────────────────────────────────────────────────────

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_tokenFile)) return;

                var json = File.ReadAllText(_tokenFile);
                var dict = JsonSerializer.Deserialize<Dictionary<string, TokenRecord>>(json);
                if (dict == null) return;

                foreach (var (shop, record) in dict)
                {
                    try
                    {
                        var token = Decrypt(record.EncryptedToken, record.IV);
                        _store[shop] = record;
                        _cache[shop] = token;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Could not decrypt token for shop {Shop} — " +
                            "will require OAuth reconnect.", shop);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not load Shopify tokens from {Path} — starting empty.", _tokenFile);
            }
        }

        private void Persist()
        {
            try
            {
                lock (_writeLock)
                {
                    var json = JsonSerializer.Serialize(
                        new Dictionary<string, TokenRecord>(_store),
                        new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_tokenFile, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist Shopify tokens to {Path}", _tokenFile);
            }
        }

        /// <summary>
        /// One-time migration from the old plain-text shop_tokens.json format.
        /// Reads { "shop": "token" } pairs, encrypts them, saves to the new file,
        /// then renames the old file to shop_tokens.json.migrated so this only runs once.
        /// </summary>
        private void MigrateFromLegacyFile(string legacyPath)
        {
            if (!File.Exists(legacyPath)) return;

            try
            {
                var json = File.ReadAllText(legacyPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null || dict.Count == 0) return;

                var imported = 0;
                foreach (var (shop, token) in dict)
                {
                    var normalized = Normalize(shop);
                    if (_cache.ContainsKey(normalized)) continue; // already loaded from new file
                    if (string.IsNullOrWhiteSpace(token)) continue;

                    var (enc, iv) = Encrypt(token);
                    _store[normalized] = new TokenRecord(enc, iv);
                    _cache[normalized] = token;
                    imported++;
                }

                if (imported > 0)
                {
                    Persist(); // write all newly-imported tokens to shopify_tokens.json
                    _logger.LogInformation(
                        "Migrated {Count} Shopify token(s) from legacy {Path} → {New}",
                        imported, legacyPath, _tokenFile);
                }

                // Rename old file so migration doesn't run again
                File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not migrate legacy token file {Path}", legacyPath);
            }
        }
    }
}
