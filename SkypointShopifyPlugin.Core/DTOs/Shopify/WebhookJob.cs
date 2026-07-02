namespace SkypointShopifyPlugin.Core.DTOs.Shopify
{
    /// <summary>
    /// Represents a queued Shopify webhook processing task to be executed out-of-band.
    /// </summary>
    public class WebhookJob
    {
        public string ShopDomain { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }
}
