using System.Collections.Concurrent;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// In-memory token cache backed by a persistent credential store.
    ///
    /// Tokens live in memory only (fast, no disk I/O on every carrier rate call).
    /// Credentials (username + encrypted password) are persisted to disk via
    /// ISkypointCredentialStore whenever SaveCredentials is called.
    ///
    /// On server restart the bootstrap service re-authenticates every known shop
    /// using the persisted credentials, so carrier rates work immediately without
    /// any hardcoded values or manual re-login.
    /// </summary>
    public class SkypointTokenStore : ISkypointTokenStore
    {
        private record TokenEntry(string Token, DateTime Expiration);

        private readonly ConcurrentDictionary<string, TokenEntry> _tokens =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ISkypointCredentialStore _credentialStore;

        public SkypointTokenStore(ISkypointCredentialStore credentialStore)
            => _credentialStore = credentialStore;

        // ── ISkypointTokenStore ──────────────────────────────────────────────────

        public void SaveCredentials(string shopDomain, string username, string password)
            => _credentialStore.Save(shopDomain, username, password);

        public void SaveToken(string shopDomain, string skypointToken, DateTime expiration)
            => _tokens[Normalize(shopDomain)] = new TokenEntry(skypointToken, expiration);

        public string? GetToken(string shopDomain)
        {
            var key = Normalize(shopDomain);
            if (_tokens.TryGetValue(key, out var entry) && entry.Expiration > DateTime.UtcNow)
                return entry.Token;
            return null;
        }

        public (string username, string password)? GetCredentials(string shopDomain)
        {
            var creds = _credentialStore.Get(shopDomain);
            if (creds == null) return null;
            return (creds.Value.Username, creds.Value.Password);
        }

        public bool IsTokenValid(string shopDomain)
            => _tokens.TryGetValue(Normalize(shopDomain), out var e) && e.Expiration > DateTime.UtcNow;

        public void Clear(string shopDomain)
        {
            _tokens.TryRemove(Normalize(shopDomain), out _);
            _credentialStore.Remove(shopDomain);
        }

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');
    }
}
