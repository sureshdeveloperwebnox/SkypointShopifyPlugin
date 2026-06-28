using System.Collections.Concurrent;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// In-memory token cache keyed by shop domain.
    /// Tokens are fetched dynamically via client_credentials on demand —
    /// no persistent storage or hardcoded values needed.
    /// </summary>
    public class ShopTokenStore : IShopTokenStore
    {
        private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);

        public void SaveToken(string shopDomain, string accessToken)
            => _tokens[shopDomain] = accessToken;

        public string? GetToken(string shopDomain)
        {
            _tokens.TryGetValue(shopDomain, out var token);
            return token;
        }

        public bool HasToken(string shopDomain) => _tokens.ContainsKey(shopDomain);

        public void RemoveToken(string shopDomain)
            => _tokens.TryRemove(shopDomain, out _);
    }
}
