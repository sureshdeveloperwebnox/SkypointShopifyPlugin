namespace SkypointShopifyPlugin.Core.Configuration
{
    public class ShopifySettings
    {
        public const string SectionName = "Shopify";
        
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string Scopes { get; set; } = "read_orders,write_orders,read_products,write_products,read_shipping,write_shipping";
        public string RedirectUri { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
    }
}
