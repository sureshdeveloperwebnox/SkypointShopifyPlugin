using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class RateRequest
    {
        [JsonPropertyName("pickUpSuburb")]
        public string PickUpSuburb { get; set; } = string.Empty;

        [JsonPropertyName("pickUpPostalCode")]
        public string PickUpPostalCode { get; set; } = string.Empty;

        [JsonPropertyName("dropOffSuburb")]
        public string DropOffSuburb { get; set; } = string.Empty;

        /// <summary>Note: Skypoint API uses "dropOver" (not "dropOff") in the field name.</summary>
        [JsonPropertyName("dropOverPostalCode")]
        public string DropOverPostalCode { get; set; } = string.Empty;

        [JsonPropertyName("parcelsDims")]
        public List<ParcelDimension> ParcelsDims { get; set; } = new();
    }

    public class ParcelDimension
    {
        [JsonPropertyName("parcel_mass")]
        public double ParcelMass { get; set; }
        [JsonPropertyName("parcel_length")]
        public double ParcelLength { get; set; }
        [JsonPropertyName("parcel_breadth")]
        public double ParcelBreadth { get; set; }
        [JsonPropertyName("parcel_height")]
        public double ParcelHeight { get; set; }
        [JsonPropertyName("predefinedParcel")]
        public string PredefinedParcel { get; set; } = string.Empty;
        [JsonPropertyName("parcel_reference")]
        public string ParcelReference { get; set; } = string.Empty;
        [JsonPropertyName("selected_parcel")]
        public string SelectedParcel { get; set; } = string.Empty;
    }
}
