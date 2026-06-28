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
    /// Options injected by the WebAPI host so the file lands in
    /// ContentRootPath/data — never inside bin\ (wiped on build).
    /// </summary>
    public class SkypointCredentialStoreOptions
    {
        public string DataDirectory { get; set; } =
            Path.Combine(AppContext.BaseDirectory, "data");
    }

    /// <summary>
    /// Persists Skypoint credentials (username + AES-encrypted password) to
    /// skypoint_credentials.json keyed by shop domain.
    ///
    /// Encryption key is derived from a random secret stored in
    /// skypoint_key.bin next to the credentials file.  The key file is
    /// created once on first run and never leaves the server — no secrets
    /// in code, config, or environment variables.
    ///
    /// Flow:
    ///   1. Merchant logs in on the dashboard  → SaveCredentials is called
    ///   2. Credentials encrypted and written to disk immediately
    ///   3. On next server restart the bootstrap service reads all shops,
    ///      decrypts, re-authenticates and warms the in-memory token cache
    /// </summary>
    public class SkypointCredentialStore : ISkypointCredentialStore
    {
        private record CredentialRecord(string Username, string EncryptedPassword, string IV);

        private readonly string _credFile;
        private readonly string _keyFile;
        private readonly ILogger<SkypointCredentialStore> _logger;
        private readonly ConcurrentDictionary<string, CredentialRecord> _store;
        private readonly object _writeLock = new();
        private readonly byte[] _encryptionKey;

        public SkypointCredentialStore(
            IOptions<SkypointCredentialStoreOptions> options,
            ILogger<SkypointCredentialStore> logger)
        {
            _logger = logger;

            var dir = options.Value.DataDirectory;
            Directory.CreateDirectory(dir);
            _credFile = Path.Combine(dir, "skypoint_credentials.json");
            _keyFile  = Path.Combine(dir, "skypoint_key.bin");

            _encryptionKey = LoadOrCreateKey();
            _store = Load();

            _logger.LogInformation(
                "SkypointCredentialStore loaded {Count} shop credential(s) from {Path}",
                _store.Count, _credFile);
        }

        // ── ISkypointCredentialStore ─────────────────────────────────────────────

        public void Save(string shopDomain, string username, string password)
        {
            shopDomain = Normalize(shopDomain);
            var (enc, iv) = Encrypt(password);
            _store[shopDomain] = new CredentialRecord(username, enc, iv);
            Persist();
            _logger.LogInformation("Skypoint credentials saved for shop: {Shop}", shopDomain);
        }

        public (string Username, string Password)? Get(string shopDomain)
        {
            if (!_store.TryGetValue(Normalize(shopDomain), out var rec))
                return null;

            try
            {
                var pwd = Decrypt(rec.EncryptedPassword, rec.IV);
                return (rec.Username, pwd);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt credentials for shop {Shop}", shopDomain);
                return null;
            }
        }

        public IReadOnlyList<string> GetAllShops()
            => _store.Keys.ToList();

        public void Remove(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            _store.TryRemove(shopDomain, out _);
            Persist();
            _logger.LogInformation("Skypoint credentials removed for shop: {Shop}", shopDomain);
        }

        // ── Key management ───────────────────────────────────────────────────────

        /// <summary>
        /// Loads the 256-bit AES key from disk, creating it on first run.
        /// The key file should be added to .gitignore and never committed.
        /// </summary>
        private byte[] LoadOrCreateKey()
        {
            if (File.Exists(_keyFile))
            {
                var existing = File.ReadAllBytes(_keyFile);
                if (existing.Length == 32)
                    return existing;
                _logger.LogWarning("Key file corrupt — regenerating. Existing credentials will be unreadable.");
            }

            var key = RandomNumberGenerator.GetBytes(32); // 256-bit AES key
            File.WriteAllBytes(_keyFile, key);
            _logger.LogInformation("Generated new Skypoint credential encryption key at {Path}", _keyFile);
            return key;
        }

        // ── AES-GCM encrypt/decrypt ──────────────────────────────────────────────

        private (string EncryptedBase64, string IvBase64) Encrypt(string plaintext)
        {
            var iv = RandomNumberGenerator.GetBytes(12); // 96-bit nonce for AES-GCM
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[16];

            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Encrypt(iv, plaintextBytes, ciphertext, tag);

            // Store tag appended to ciphertext for simplicity
            var combined = new byte[ciphertext.Length + tag.Length];
            ciphertext.CopyTo(combined, 0);
            tag.CopyTo(combined, ciphertext.Length);

            return (Convert.ToBase64String(combined), Convert.ToBase64String(iv));
        }

        private string Decrypt(string encryptedBase64, string ivBase64)
        {
            var combined = Convert.FromBase64String(encryptedBase64);
            var iv = Convert.FromBase64String(ivBase64);

            var tagLength = 16;
            var ciphertext = combined[..^tagLength];
            var tag = combined[^tagLength..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Decrypt(iv, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }

        // ── File I/O ─────────────────────────────────────────────────────────────

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');

        private ConcurrentDictionary<string, CredentialRecord> Load()
        {
            try
            {
                if (File.Exists(_credFile))
                {
                    var json = File.ReadAllText(_credFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, CredentialRecord>>(json);
                    if (dict != null)
                        return new ConcurrentDictionary<string, CredentialRecord>(
                            dict, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read credential store from {Path} — starting empty", _credFile);
            }

            return new ConcurrentDictionary<string, CredentialRecord>(StringComparer.OrdinalIgnoreCase);
        }

        private void Persist()
        {
            try
            {
                lock (_writeLock)
                {
                    var json = JsonSerializer.Serialize(
                        new Dictionary<string, CredentialRecord>(_store),
                        new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_credFile, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist credential store to {Path}", _credFile);
            }
        }
    }
}
