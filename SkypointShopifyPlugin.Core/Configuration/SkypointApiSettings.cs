namespace SkypointShopifyPlugin.Core.Configuration
{
    public class SkypointApiSettings
    {
        public const string SectionName = "SkypointApi";
        
        public string BaseUrl { get; set; } = "https://uat.skypoint.online";
        public string LoginEndpoint { get; set; } = "/api/service/session/customer/login";
        public string RateEndpoint { get; set; } = "/api/service/rate/engine/quote";
        public string BookingEndpoint { get; set; } = "/api/service/booking/create";
        public int TimeoutSeconds { get; set; } = 30;
    }
}
