using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Infrastructure.Services;

namespace SkypointShopifyPlugin.Infrastructure.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<SkypointApiSettings>(options =>
            {
                configuration.GetSection(SkypointApiSettings.SectionName).Bind(options);
            });

            services.Configure<ShopifySettings>(options =>
            {
                configuration.GetSection(ShopifySettings.SectionName).Bind(options);
            });

            services.Configure<SkypointOrderStoreOptions>(options =>
            {
                configuration.GetSection(SkypointOrderStoreOptions.SectionName).Bind(options);
            });
            
            services.AddHttpClient<ISkypointApiClient, SkypointApiClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddHttpClient<IShopifyOAuthService, ShopifyOAuthService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddHttpClient<IShopifyAdminService, ShopifyAdminService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // Centralized Encryption Service
            services.AddSingleton<IEncryptionService, EncryptionService>();

            // Shopify OAuth token store — in-memory cache + AES-encrypted file persistence.
            // Token is written on OAuth callback and reloaded on every server restart.
            // Nothing is ever sent to the browser.
            services.AddSingleton<IShopTokenStore, ShopTokenStore>();

            // Skypoint credential store — persists encrypted credentials to disk.
            // DataDirectory shares the same directory as the Shopify token store.
            services.AddSingleton<ISkypointCredentialStore, SkypointCredentialStore>();

            // In-memory token cache backed by the persistent credential store above.
            services.AddSingleton<ISkypointTokenStore, SkypointTokenStore>();

            // Skypoint order store — persists orders to JSON files in data directory.
            // Shares the same data directory structure as token stores.
            services.AddSingleton<ISkypointOrderStore, SkypointOrderStore>();

            // Skypoint order service — handles order creation, processing, and management.
            services.AddScoped<ISkypointOrderService, SkypointOrderService>();

            // Configuration store — persists app configuration to disk.
            services.AddSingleton<IConfigurationStore, ConfigurationStore>();

            // On startup: loads saved configuration from app_config.json
            services.AddHostedService<ConfigurationBootstrapService>();

            // On startup: re-authenticates every known shop from persisted credentials.
            // No hardcoded values — scales to any number of shops automatically.
            services.AddHostedService<SkypointTokenBootstrapService>();

            // On startup: auto-registers/updates the Shopify carrier service for every
            // shop that has a stored OAuth token. Permanently fixes the "reinstall required"
            // banner caused by server restarts or ngrok URL changes.
            services.AddHostedService<CarrierServiceBootstrapService>();

            // Webhook in-memory background processing queue
            services.AddSingleton<IWebhookQueue, WebhookQueue>();
            services.AddHostedService<WebhookQueueProcessor>();

            return services;
        }
    }
}
