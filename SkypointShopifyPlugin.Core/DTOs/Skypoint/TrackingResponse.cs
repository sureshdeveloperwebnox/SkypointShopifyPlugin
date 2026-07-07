using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class TrackingResponse
    {
        [JsonPropertyName("trackingInfo")]
        public List<TrackingEventDto> TrackingInfo { get; set; } = new();

        [JsonPropertyName("booking")]
        public BookingResponse? Booking { get; set; }
    }

    public class TrackingEventDto
    {
        [JsonPropertyName("WaybillEventDate")]
        public string WaybillEventDate { get; set; } = string.Empty;

        [JsonPropertyName("WaybillEventTime")]
        public string WaybillEventTime { get; set; } = string.Empty;

        [JsonPropertyName("WaybillEventDescription")]
        public string WaybillEventDescription { get; set; } = string.Empty;

        [JsonPropertyName("WaybillEventBranch")]
        public string WaybillEventBranch { get; set; } = string.Empty;

        [JsonPropertyName("WaybillEventId")]
        public long WaybillEventId { get; set; }
    }
}
