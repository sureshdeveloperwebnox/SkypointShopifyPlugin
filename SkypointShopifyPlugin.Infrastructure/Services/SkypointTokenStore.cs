using System;
using System.Collections.Concurrent;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// In-memory cache for temporary Skypoint tokens.
    /// Ephemeral tokens do not need database or disk storage.
    /// </summary>
    public class SkypointTokenStore : ISkypointTokenStore
    {
        private readonly ISkypointCredentialStore _credentialStore;
        private readonly ConcurrentDictionary<string, TokenCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

        public SkypointTokenStore(ISkypointCredentialStore credentialStore)
        {
            _credentialStore = credentialStore;
        }

        // ── ISkypointTokenStore ──────────────────────────────────────────────────

        public void SaveCredentials(string shopDomain, string username, string password)
            => _credentialStore.Save(shopDomain, username, password);

        public void SaveToken(string shopDomain, string skypointToken, DateTime expiration, string? userId = null)
        {
            shopDomain = Normalize(shopDomain);
            _cache[shopDomain] = new TokenCacheEntry(skypointToken, expiration, userId);
        }

        public string? GetToken(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            if (_cache.TryGetValue(shopDomain, out var entry) && entry.Expiration > DateTime.UtcNow)
            {
                return entry.Token;
            }
            return null;
        }

        public string? GetUserId(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            if (_cache.TryGetValue(shopDomain, out var entry) && entry.Expiration > DateTime.UtcNow)
            {
                return entry.UserId;
            }
            return null;
        }

        public (string username, string password)? GetCredentials(string shopDomain)
        {
            var creds = _credentialStore.Get(shopDomain);
            if (creds == null) return null;
            return (creds.Value.Username, creds.Value.Password);
        }

        public bool IsTokenValid(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            return _cache.TryGetValue(shopDomain, out var entry) && entry.Expiration > DateTime.UtcNow;
        }

        public void Clear(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);
            _cache.TryRemove(shopDomain, out _);
            _credentialStore.Remove(shopDomain);
        }

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');

        private record TokenCacheEntry(string Token, DateTime Expiration, string? UserId);
    }
}
