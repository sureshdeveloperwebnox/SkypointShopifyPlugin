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
            
            services.AddHttpClient<ISkypointApiClient, SkypointApiClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddHttpClient<IShopifyOAuthService, ShopifyOAuthService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            return services;
        }
    }
}
