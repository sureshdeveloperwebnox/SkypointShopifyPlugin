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

        /// <summary>
        /// Additional legacy app credentials to try when fetching tokens.
        /// Format: pipe-separated "clientId|clientSecret" pairs, comma-separated.
        /// Example: id1|secret1,id2|secret2
        /// </summary>
        public string LegacyCredentials { get; set; } = string.Empty;

        public List<(string ClientId, string ClientSecret)> GetAllCredentials()
        {
            var result = new List<(string, string)>();
            if (!string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret))
                result.Add((ClientId, ClientSecret));

            if (!string.IsNullOrEmpty(LegacyCredentials))
            {
                foreach (var pair in LegacyCredentials.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Trim().Split('|');
                    if (parts.Length == 2)
                        result.Add((parts[0].Trim(), parts[1].Trim()));
                }
            }
            return result;
        }
    }
}
