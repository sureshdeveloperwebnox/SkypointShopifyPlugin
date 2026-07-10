using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class BookingRequest
    {
        public string UserId { get; set; } = string.Empty;
        [JsonPropertyName("pickUpAddress")]
        public string PickUpAddress { get; set; } = string.Empty;
        [JsonPropertyName("dropOffAddress")]
        public string DropOffAddress { get; set; } = string.Empty;
        [JsonPropertyName("fromSuburb")]
        public string FromSuburb { get; set; } = string.Empty;
        [JsonPropertyName("toSuburb")]
        public string ToSuburb { get; set; } = string.Empty;
        [JsonPropertyName("pickUpPCode")]
        public string PickUpPCode { get; set; } = string.Empty;
        [JsonPropertyName("dropOffPCode")]
        public string DropOffPCode { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        [JsonPropertyName("destinationProvince")]
        public string DestinationProvince { get; set; } = string.Empty;
        [JsonPropertyName("dropOff")]
        public DropOffPerson DropOff { get; set; } = new();
        [JsonPropertyName("pickUp")]
        public PickUpPerson PickUp { get; set; } = new();
        public string Type { get; set; } = "ROAD";
        [JsonPropertyName("pickUpDate")]
        public string PickUpDate { get; set; } = string.Empty;
        [JsonPropertyName("pickUpTime")]
        public string PickUpTime { get; set; } = string.Empty;
        [JsonPropertyName("parcelDimensions")]
        public List<ParcelDimension> ParcelDimensions { get; set; } = new();
        [JsonPropertyName("pickUpCity")]
        public string PickUpCity { get; set; } = string.Empty;
        [JsonPropertyName("dropOffCity")]
        public string DropOffCity { get; set; } = string.Empty;
        [JsonPropertyName("pickUpzip")]
        public string PickUpZip { get; set; } = string.Empty;
        [JsonPropertyName("dropOffzip")]
        public string DropOffZip { get; set; } = string.Empty;
        [JsonPropertyName("shipmentType")]
        public string ShipmentType { get; set; } = string.Empty;
        [JsonPropertyName("toCounterCode")]
        public string ToCounterCode { get; set; } = string.Empty;
        [JsonPropertyName("toCounterName")]
        public string ToCounterName { get; set; } = string.Empty;
        [JsonPropertyName("saIdNumber")]
        public string SaIdNumber { get; set; } = string.Empty;
        [JsonPropertyName("pickUpCountry")]
        public string PickUpCountry { get; set; } = string.Empty;
        [JsonPropertyName("wooCommerceOrderId")]
        public string? WooCommerceOrderId { get; set; }
    }

    public class DropOffPerson
    {
        [JsonPropertyName("base64Image")]
        public string Base64Image { get; set; } = ".";
        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;
        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("companyName")]
        public string CompanyName { get; set; } = " ";
        public string Complex { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        [JsonPropertyName("unitNo")]
        public string UnitNo { get; set; } = string.Empty;
        public string Suburb { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }

    public class PickUpPerson
    {
        [JsonPropertyName("base64Image")]
        public string Base64Image { get; set; } = ".";
        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;
        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("companyName")]
        public string CompanyName { get; set; } = " ";
        public string Complex { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        [JsonPropertyName("unitNo")]
        public string UnitNo { get; set; } = string.Empty;
        public string Suburb { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }
}
