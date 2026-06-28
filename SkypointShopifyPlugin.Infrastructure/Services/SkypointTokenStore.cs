using System.Collections.Concurrent;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Pure in-memory store. No file writes, no hardcoded values.
    /// Credentials are stored when the merchant logs in via the app dashboard.
    /// Tokens are cached and auto-refreshed using stored credentials.
    /// </summary>
    public class SkypointTokenStore : ISkypointTokenStore
    {
        private record TokenEntry(string Token, DateTime Expiration);
        private record CredentialEntry(string Username, string Password);

        private readonly ConcurrentDictionary<string, TokenEntry> _tokens = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CredentialEntry> _credentials = new(StringComparer.OrdinalIgnoreCase);

        public void SaveCredentials(string shopDomain, string username, string password)
            => _credentials[shopDomain] = new CredentialEntry(username, password);

        public void SaveToken(string shopDomain, string skypointToken, DateTime expiration)
            => _tokens[shopDomain] = new TokenEntry(skypointToken, expiration);

        public string? GetToken(string shopDomain)
        {
            if (_tokens.TryGetValue(shopDomain, out var entry) && entry.Expiration > DateTime.UtcNow)
                return entry.Token;
            return null;
        }

        public (string username, string password)? GetCredentials(string shopDomain)
        {
            if (_credentials.TryGetValue(shopDomain, out var creds))
                return (creds.Username, creds.Password);
            return null;
        }

        public bool IsTokenValid(string shopDomain)
            => _tokens.TryGetValue(shopDomain, out var e) && e.Expiration > DateTime.UtcNow;

        public void Clear(string shopDomain)
        {
            _tokens.TryRemove(shopDomain, out _);
            _credentials.TryRemove(shopDomain, out _);
        }
    }
}
