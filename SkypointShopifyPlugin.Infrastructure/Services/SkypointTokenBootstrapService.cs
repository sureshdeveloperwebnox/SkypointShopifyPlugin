using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Runs once on startup. Reads every shop that has stored Skypoint credentials
    /// from ISkypointCredentialStore, authenticates each one, and warms the
    /// in-memory token cache.
    ///
    /// No hardcoded credentials, no config values, scales to any number of shops.
    /// Credentials are written to the store when a merchant logs in via the
    /// dashboard — after that this service handles all future restarts automatically.
    /// </summary>
    public class SkypointTokenBootstrapService : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<SkypointTokenBootstrapService> _logger;

        public SkypointTokenBootstrapService(
            IServiceProvider services,
            ILogger<SkypointTokenBootstrapService> logger)
        {
            _services = services;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _services.CreateScope();
            var credentialStore = scope.ServiceProvider.GetRequiredService<ISkypointCredentialStore>();
            var tokenStore      = scope.ServiceProvider.GetRequiredService<ISkypointTokenStore>();
            var apiClient       = scope.ServiceProvider.GetRequiredService<ISkypointApiClient>();

            var shops = credentialStore.GetAllShops();

            if (shops.Count == 0)
            {
                _logger.LogInformation(
                    "No Skypoint credentials on disk — carrier rates will be available " +
                    "after the merchant logs in via the Skypoint Shipping dashboard.");
                return;
            }

            _logger.LogInformation(
                "Bootstrapping Skypoint tokens for {Count} known shop(s)...", shops.Count);

            var tasks = shops.Select(shop => AuthenticateShopAsync(
                shop, credentialStore, tokenStore, apiClient, cancellationToken));

            await Task.WhenAll(tasks);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        // ── private ─────────────────────────────────────────────────────────────

        private async Task AuthenticateShopAsync(
            string shop,
            ISkypointCredentialStore credentialStore,
            ISkypointTokenStore tokenStore,
            ISkypointApiClient apiClient,
            CancellationToken ct)
        {
            var creds = credentialStore.Get(shop);
            if (creds == null)
            {
                _logger.LogWarning("No credentials found for shop {Shop} — skipping", shop);
                return;
            }

            try
            {
                var loginResponse = await apiClient.LoginAsync(new LoginRequest
                {
                    Username = creds.Value.Username,
                    Pwd      = creds.Value.Password
                });

                if (loginResponse?.Token?.TokenValue == null)
                {
                    _logger.LogWarning(
                        "Bootstrap login returned no token for shop {Shop} — " +
                        "merchant may need to log in again if credentials changed.", shop);
                    return;
                }

                tokenStore.SaveToken(shop, loginResponse.Token.TokenValue, loginResponse.Token.Expiration, loginResponse.Id);
                _logger.LogInformation("Token bootstrapped for shop: {Shop} (expires {Exp:u})",
                    shop, loginResponse.Token.Expiration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to bootstrap token for shop {Shop} — " +
                    "carrier rates will still work once the merchant logs in via the dashboard.", shop);
            }
        }
    }
}
