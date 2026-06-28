namespace SkypointShopifyPlugin.Core.Configuration
{
    /// <summary>
    /// Centralized configuration for Skypoint API integration
    /// All API endpoints and settings are configured here for easy maintenance
    /// </summary>
    public class SkypointApiSettings
    {
        public const string SectionName = "SkypointApi";
        
        /// <summary>
        /// Base URL for Skypoint API
        /// </summary>
        public string BaseUrl { get; set; } = "https://uat.skypoint.online";

        /// <summary>
        /// Login endpoint path
        /// </summary>
        public string LoginEndpoint { get; set; } = "/api/service/session/customer/login";

        /// <summary>
        /// Register endpoint path
        /// </summary>
        public string RegisterEndpoint { get; set; } = "/api/service/session/customer/register";

        /// <summary>
        /// Rate quote endpoint path
        /// </summary>
        public string RateEndpoint { get; set; } = "/api/service/rate/engine/quote";

        /// <summary>
        /// Booking creation endpoint path
        /// </summary>
        public string BookingEndpoint { get; set; } = "/api/service/booking/create";

        /// <summary>
        /// Request timeout in seconds
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum retry attempts for failed requests
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Default predefined parcel type for rate requests
        /// </summary>
        public string DefaultParcelType { get; set; } = "A4_Text_Book";

        /// <summary>
        /// Gets the full login URL
        /// </summary>
        public string GetLoginUrl() => $"{BaseUrl}{LoginEndpoint}";

        /// <summary>
        /// Gets the full register URL
        /// </summary>
        public string GetRegisterUrl() => $"{BaseUrl}{RegisterEndpoint}";

        /// <summary>
        /// Gets the full rate quote URL
        /// </summary>
        public string GetRateQuoteUrl() => $"{BaseUrl}{RateEndpoint}";

        /// <summary>
        /// Gets the full booking URL
        /// </summary>
        public string GetBookingUrl() => $"{BaseUrl}{BookingEndpoint}";
    }
}
