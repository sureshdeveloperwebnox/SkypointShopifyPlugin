using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Infrastructure.Data;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Database-backed Skypoint token cache.
    /// Eliminates in-memory caching state drift across multiple scaled-out web instances.
    /// </summary>
    public class SkypointTokenStore : ISkypointTokenStore
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISkypointCredentialStore _credentialStore;
        private readonly ILogger<SkypointTokenStore> _logger;

        public SkypointTokenStore(
            IServiceScopeFactory scopeFactory,
            ISkypointCredentialStore credentialStore,
            ILogger<SkypointTokenStore> logger)
        {
            _scopeFactory = scopeFactory;
            _credentialStore = credentialStore;
            _logger = logger;
        }

        // ── ISkypointTokenStore ──────────────────────────────────────────────────

        public void SaveCredentials(string shopDomain, string username, string password)
            => _credentialStore.Save(shopDomain, username, password);

        public void SaveToken(string shopDomain, string skypointToken, DateTime expiration, string? userId = null)
        {
            shopDomain = Normalize(shopDomain);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.SkypointTokens.FirstOrDefault(t => t.ShopDomain == shopDomain);
            if (entity == null)
            {
                entity = new SkypointTokenEntity
                {
                    ShopDomain = shopDomain,
                    Token = skypointToken,
                    Expiration = expiration,
                    UserId = userId,
                    UpdatedAt = DateTime.UtcNow
                };
                db.SkypointTokens.Add(entity);
            }
            else
            {
                entity.Token = skypointToken;
                entity.Expiration = expiration;
                entity.UserId = userId;
                entity.UpdatedAt = DateTime.UtcNow;
                db.SkypointTokens.Update(entity);
            }

            db.SaveChanges();
            _logger.LogInformation("Saved Skypoint token to database for shop: {Shop}", shopDomain);
        }

        public string? GetToken(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.SkypointTokens.FirstOrDefault(t => t.ShopDomain == shopDomain);
            if (entity != null && entity.Expiration > DateTime.UtcNow)
            {
                return entity.Token;
            }

            return null;
        }

        public string? GetUserId(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.SkypointTokens.FirstOrDefault(t => t.ShopDomain == shopDomain);
            if (entity != null && entity.Expiration > DateTime.UtcNow)
            {
                return entity.UserId;
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

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            return db.SkypointTokens.Any(t => t.ShopDomain == shopDomain && t.Expiration > DateTime.UtcNow);
        }

        public void Clear(string shopDomain)
        {
            shopDomain = Normalize(shopDomain);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.SkypointTokens.FirstOrDefault(t => t.ShopDomain == shopDomain);
            if (entity != null)
            {
                db.SkypointTokens.Remove(entity);
                db.SaveChanges();
                _logger.LogInformation("Cleared Skypoint token from database for shop: {Shop}", shopDomain);
            }

            _credentialStore.Remove(shopDomain);
        }

        private static string Normalize(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');
    }
}
