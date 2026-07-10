namespace SkypointShopifyPlugin.Core.Configuration
{
    /// <summary>
    /// Centralized configuration for Skypoint API integration
    /// All API endpoints and settings are configured via appsettings.json or environment variables
    /// Default values provided for development - should be overridden in production
    /// </summary>
    public class SkypointApiSettings
    {
        public const string SectionName = "SkypointApi";

        /// <summary>
        /// Base URL for Skypoint API (defaults to UAT environment)
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
        /// Booking tracking endpoint path
        /// </summary>
        public string TrackingEndpoint { get; set; } = "/api/service/booking/track";

        /// <summary>
        /// Selected PUDO point endpoint path
        /// </summary>
        public string PudoEndpoint { get; set; } = "/api/service/session/selected/pudo-point";

        /// <summary>
        /// Base URL for PUDO widget map selector
        /// </summary>
        public string PudoWidgetBaseUrl { get; set; } = "https://eocloudx.co.za/SkyOnlineSelect_Test";

        /// <summary>
        /// Request timeout in seconds (optional - defaults to 30)
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum retry attempts for failed requests (optional - defaults to 3)
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

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

        /// <summary>Gets the full booking URL</summary>
        public string GetBookingUrl() => $"{BaseUrl}{BookingEndpoint}";

        /// <summary>Gets the full booking tracking URL for a specific track/waybill number</summary>
        public string GetTrackingUrl(string trackNo) => $"{BaseUrl}{TrackingEndpoint}/{trackNo}";

        /// <summary>Gets the full selected PUDO point URL for a specific session GUID</summary>
        public string GetPudoSelectedUrl(string guid) => $"{BaseUrl}{PudoEndpoint}/{guid}";

        /// <summary>Gets the full waybill download URL for a specific waybill number</summary>
        public string GetWaybillDownloadUrl(string waybillNumber) => $"{BaseUrl}/api/service/booking/download/waybill/{waybillNumber}";

        /// <summary>Gets the bulk label printing URL</summary>
        public string GetBulkLabelPrintUrl() => $"{BaseUrl}/api/service/booking/bulk/label/printing";

        /// <summary>Gets the full booking details URL for a specific booking ID</summary>
        public string GetBookingDetailsUrl(string bookingId) => $"{BaseUrl}/api/service/booking/{bookingId}";

        /// <summary>Gets the full process booking / wallet pay URL for a specific track number</summary>
        public string GetProcessBookingUrl(string trackNo) => $"{BaseUrl}/api/service/booking/process/{trackNo}";
    }
}
