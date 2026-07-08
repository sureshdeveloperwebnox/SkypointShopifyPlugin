using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    public record CheckoutLineItemDto(long VariantId, int Quantity);

    public interface IShopifyAdminService
    {
        Task<bool> RegisterCarrierServiceAsync(string shopDomain, string accessToken, string carrierServiceUrl);
        Task<(bool success, string message)> RegisterAndAssignCarrierServiceAsync(string shopDomain, string accessToken, string carrierServiceUrl);
        Task<(bool success, string message)> SyncWebhooksAsync(string shopDomain, string accessToken, string publicBaseUrl);
        Task<(bool success, string message)> SyncScriptTagsAsync(string shopDomain, string accessToken, string publicBaseUrl);
        Task<string> GetOrdersJsonAsync(string shopDomain, string accessToken, DateTime? since = null);
        Task<bool> UpdateOrderTrackingAsync(string shopDomain, string accessToken, string shopifyOrderId, string trackNo, string carrierName, string trackingUrl);
        Task<string?> CreateCheckoutWithAddressAsync(
            string shopDomain,
            string accessToken,
            string address1,
            string address2,
            string city,
            string zip,
            string countryCode,
            string? firstName,
            string? lastName,
            IEnumerable<CheckoutLineItemDto> lineItems);
    }
}

