namespace SkypointShopifyPlugin.Core.DTOs.Configuration
{
    /// <summary>
    /// Centralized application configuration model
    /// Used for saving/loading configuration via UI
    /// </summary>
    public class AppConfiguration
    {
        public ShopifyConfig Shopify { get; set; } = new();
        public SkypointApiConfig SkypointApi { get; set; } = new();
        public SkypointMappingsConfig SkypointMappings { get; set; } = new();
    }

    public class ShopifyConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
    }

    public class SkypointApiConfig
    {
        public string BaseUrl { get; set; } = "https://uat.skypoint.online";
        public string LoginEndpoint { get; set; } = "/api/service/session/customer/login";
        public string RegisterEndpoint { get; set; } = "/api/service/session/customer/register";
        public string RateEndpoint { get; set; } = "/api/service/rate/engine/quote";
        public string BookingEndpoint { get; set; } = "/api/service/booking/create";
        public string PudoWidgetBaseUrl { get; set; } = "https://eocloudx.co.za/SkyPointSelectNoMap";
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxRetryAttempts { get; set; } = 3;
    }

    public class SkypointMappingsConfig
    {
        public double DefaultParcelLength { get; set; } = 10.0;
        public double DefaultParcelBreadth { get; set; } = 10.0;
        public double DefaultParcelHeight { get; set; } = 10.0;
        public double DefaultParcelMass { get; set; } = 0.5;
        public string DefaultParcelType { get; set; } = "A4_Text_Book";
        
        public Dictionary<string, string> PostalCodeMappings { get; set; } = new();
        public Dictionary<string, string> SuburbMappings { get; set; } = new();
    }
}
