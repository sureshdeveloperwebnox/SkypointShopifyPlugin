using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class PudoPointResponse
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("addr1")]
        public string Addr1 { get; set; } = string.Empty;

        [JsonPropertyName("addr2")]
        public string Addr2 { get; set; } = string.Empty;

        [JsonPropertyName("addr3")]
        public string Addr3 { get; set; } = string.Empty;

        [JsonPropertyName("suburb")]
        public string Suburb { get; set; } = string.Empty;

        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("pcode")]
        public string Pcode { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;
    }
}
