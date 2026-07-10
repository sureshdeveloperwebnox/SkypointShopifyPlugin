namespace SkypointShopifyPlugin.Core.DTOs.Shopify
{
    public class ShopifyTokenResponse
    {
        public string access_token { get; set; } = string.Empty;
        public string scope { get; set; } = string.Empty;
        public string? refresh_token { get; set; }
        public int? expires_in { get; set; }
    }
}
