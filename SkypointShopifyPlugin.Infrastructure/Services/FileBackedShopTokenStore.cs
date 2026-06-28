using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// File-backed Shopify OAuth token store.
    /// Persists per-store access tokens to data/shop_tokens.json so they survive
    /// server restarts. No .env or hardcoded credentials needed — tokens are
    /// obtained once via the OAuth code-exchange flow and stored here automatically.
    /// </summary>
    public class FileBackedShopTokenStore : IShopTokenStore
    {
        private readonly string _filePath;
        private readonly ILogger<FileBackedShopTokenStore> _logger;
        private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public FileBackedShopTokenStore(IConfiguration configuration, ILogger<FileBackedShopTokenStore> logger)
        {
            _logger = logger;

            // Resolve storage directory: configurable via "ShopTokenStore:DataPath",
            // defaults to a "data" folder next to the executable.
            var dataPath = configuration["ShopTokenStore:DataPath"]
                ?? Path.Combine(AppContext.BaseDirectory, "data");

            Directory.CreateDirectory(dataPath);
            _filePath = Path.Combine(dataPath, "shop_tokens.json");
            LoadFromDisk();
        }

        /// <summary>Saves a token to memory and immediately flushes to disk.</summary>
        public void SaveToken(string shopDomain, string accessToken)
        {
            _cache[Normalize(shopDomain)] = accessToken;
            _ = FlushToDiskAsync(); // fire-and-forget; errors are logged, not thrown
        }

        /// <summary>Returns the stored OAuth token for a shop, or null if not found.</summary>
        public string? GetToken(string shopDomain)
        {
            _cache.TryGetValue(Normalize(shopDomain), out var token);
            return token;
        }

        public bool HasToken(string shopDomain) => _cache.ContainsKey(Normalize(shopDomain));

        public void RemoveToken(string shopDomain)
        {
            _cache.TryRemove(Normalize(shopDomain), out _);
            _ = FlushToDiskAsync(); // fire-and-forget; errors are logged, not thrown
        }

        // ── internals ──────────────────────────────────────────────────────────────

        private static string Normalize(string shop)
            => shop.Replace("https://", "").Replace("http://", "").TrimEnd('/').ToLowerInvariant();

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                var json = File.ReadAllText(_filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null) return;

                foreach (var kv in dict)
                    _cache[kv.Key] = kv.Value;

                _logger.LogInformation("FileBackedShopTokenStore: loaded {Count} shop token(s) from {File}",
                    dict.Count, _filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileBackedShopTokenStore: could not load {File} — starting empty", _filePath);
            }
        }

        private async Task FlushToDiskAsync()
        {
            await _writeLock.WaitAsync();
            try
            {
                var snapshot = new Dictionary<string, string>(_cache);
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_filePath, json);
                _logger.LogDebug("FileBackedShopTokenStore: flushed {Count} token(s) to {File}",
                    snapshot.Count, _filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileBackedShopTokenStore: could not write {File}", _filePath);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
