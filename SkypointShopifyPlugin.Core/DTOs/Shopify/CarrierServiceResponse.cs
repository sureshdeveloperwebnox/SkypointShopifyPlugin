using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Shopify
{
    public class CarrierServiceResponse
    {
        [JsonPropertyName("rates")]
        public List<ShippingRate> Rates { get; set; } = new();
    }

    public class ShippingRate
    {
        [JsonPropertyName("service_name")]
        public string ServiceName { get; set; } = string.Empty;

        [JsonPropertyName("service_code")]
        public string ServiceCode { get; set; } = string.Empty;

        /// <summary>
        /// Price in cents (e.g. 1500 = $15.00). Shopify requires an integer in the smallest currency unit.
        /// </summary>
        [JsonPropertyName("total_price")]
        public int TotalPrice { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("min_delivery_date")]
        public string? MinDeliveryDate { get; set; }

        [JsonPropertyName("max_delivery_date")]
        public string? MaxDeliveryDate { get; set; }
    }
}
