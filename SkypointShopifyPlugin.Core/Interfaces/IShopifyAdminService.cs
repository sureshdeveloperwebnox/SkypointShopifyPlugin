namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface IShopifyAdminService
    {
        Task<bool> RegisterCarrierServiceAsync(string shopDomain, string accessToken, string carrierServiceUrl);
        Task<(bool success, string message)> RegisterAndAssignCarrierServiceAsync(string shopDomain, string accessToken, string carrierServiceUrl);
        Task<(bool success, string message)> SyncWebhooksAsync(string shopDomain, string accessToken, string publicBaseUrl);
        Task<string> GetOrdersJsonAsync(string shopDomain, string accessToken, DateTime? since = null);
    }
}
