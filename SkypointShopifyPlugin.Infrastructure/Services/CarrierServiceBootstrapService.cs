using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Runs once on startup. For every shop that already has a stored Shopify
    /// OAuth token, it automatically registers (or updates) the carrier service
    /// callback URL so live shipping rates work without any merchant action.
    ///
    /// This is the permanent fix for the "reinstall required" banner:
    ///   - Server restart         → carrier callback URL is refreshed automatically.
    ///   - ngrok URL change       → callback URL is updated automatically.
    ///   - First install          → carrier is registered during the OAuth callback
    ///                              (not here), so this service is a safety net only.
    ///
    /// If a token is revoked (Shopify returns 401), the token is cleaned up and
    /// the shop will go through the normal OAuth flow next time.
    /// </summary>
    public class CarrierServiceBootstrapService : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<CarrierServiceBootstrapService> _logger;

        public CarrierServiceBootstrapService(
            IServiceProvider services,
            ILogger<CarrierServiceBootstrapService> logger)
        {
            _services = services;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Small delay so the app is fully listening before we call back into
            // Shopify (avoids log noise if the app is slow to bind the port).
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            using var scope = _services.CreateScope();
            var shopTokenStore      = scope.ServiceProvider.GetRequiredService<IShopTokenStore>();
            var adminService        = scope.ServiceProvider.GetRequiredService<IShopifyAdminService>();
            var configuration       = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var shops = shopTokenStore.GetAllShops();

            if (shops.Count == 0)
            {
                _logger.LogInformation(
                    "CarrierServiceBootstrap: no known shops — carrier will be registered " +
                    "automatically when the first merchant completes OAuth.");
                return;
            }

            // Derive the public base URL (same logic as ShopifyController.BuildPublicBaseUrl)
            var publicBase = BuildPublicBaseUrl(configuration);

            if (!publicBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "CarrierServiceBootstrap: skipped — public base URL is not HTTPS ({Url}). " +
                    "Update Shopify__RedirectUri in .env with the current ngrok URL, then restart.",
                    publicBase);
                return;
            }

            _logger.LogInformation(
                "CarrierServiceBootstrap: auto-registering carrier service for {Count} known shop(s) → {Base}",
                shops.Count, publicBase);

            var tasks = shops.Select(shop =>
                RegisterForShopAsync(shop, shopTokenStore, adminService, publicBase, cancellationToken));

            await Task.WhenAll(tasks);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        // ── private helpers ─────────────────────────────────────────────────────

        private async Task RegisterForShopAsync(
            string shop,
            IShopTokenStore shopTokenStore,
            IShopifyAdminService adminService,
            string publicBase,
            CancellationToken ct)
        {
            var accessToken = shopTokenStore.GetToken(shop);
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("CarrierServiceBootstrap: no Shopify token in store for {Shop} — skipping", shop);
                return;
            }

            var carrierUrl = $"{publicBase}/api/carrier/rates?shop={Uri.EscapeDataString(shop)}";

            try
            {
                var (success, message) = await adminService.RegisterAndAssignCarrierServiceAsync(
                    shop, accessToken, carrierUrl);

                if (success)
                    _logger.LogInformation(
                        "CarrierServiceBootstrap ✓ {Shop}: {Msg}", shop, message);
                else
                {
                    _logger.LogWarning(
                        "CarrierServiceBootstrap ✗ {Shop}: {Msg}", shop, message);

                    // If Shopify rejected the token (401/403), clean it up so the
                    // dashboard correctly prompts the merchant to reconnect via OAuth.
                    if (IsInvalidTokenMessage(message))
                    {
                        shopTokenStore.RemoveToken(shop);
                        _logger.LogWarning(
                            "CarrierServiceBootstrap: removed invalid Shopify token for {Shop} — " +
                            "merchant must reconnect via dashboard.", shop);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CarrierServiceBootstrap: exception registering carrier for {Shop}", shop);
            }
        }

        private static bool IsInvalidTokenMessage(string message)
            => message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase)
               || message.Contains("401", StringComparison.OrdinalIgnoreCase);

        private static string BuildPublicBaseUrl(IConfiguration configuration)
        {
            var redirectUri = configuration["Shopify:RedirectUri"];
            if (!string.IsNullOrEmpty(redirectUri))
            {
                var uri = new Uri(redirectUri);
                return $"{uri.Scheme}://{uri.Host}";
            }
            // Fallback — won't be HTTPS, so bootstrap will be skipped
            return "http://localhost";
        }
    }
}
