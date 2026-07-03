using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class EcommercePlatformService : IEcommercePlatformService
    {
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IShopTokenStore _shopTokenStore;
        private readonly ILogger<EcommercePlatformService> _logger;

        public EcommercePlatformService(
            IShopifyAdminService shopifyAdminService,
            IShopTokenStore shopTokenStore,
            ILogger<EcommercePlatformService> logger)
        {
            _shopifyAdminService = shopifyAdminService;
            _shopTokenStore = shopTokenStore;
            _logger = logger;
        }

        public async Task<bool> UpdateTrackingAsync(string shopDomain, string orderId, string trackNo, string carrierName, string trackingUrl, string status)
        {
            try
            {
                _logger.LogInformation("Routing tracking update for Order {OrderId} on platform {ShopDomain}", orderId, shopDomain);

                // Check if this is a Shopify store domain
                if (shopDomain.Contains("myshopify.com", StringComparison.OrdinalIgnoreCase))
                {
                    var token = _shopTokenStore.GetToken(shopDomain);
                    if (string.IsNullOrEmpty(token))
                    {
                        _logger.LogError("Shopify access token not found for shop {ShopDomain}", shopDomain);
                        return false;
                    }

                    return await _shopifyAdminService.UpdateOrderTrackingAsync(shopDomain, token, orderId, trackNo, carrierName, trackingUrl);
                }
                else
                {
                    _logger.LogWarning("Unsupported platform for tracking updates: {ShopDomain}", shopDomain);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update platform tracking for order {OrderId} on {ShopDomain}", orderId, shopDomain);
                return false;
            }
        }
    }
}
