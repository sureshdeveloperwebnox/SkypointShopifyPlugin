using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class BookingResponse
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        public User User { get; set; } = new();
        [JsonPropertyName("pickUpAddress")]
        public string PickUpAddress { get; set; } = string.Empty;
        [JsonPropertyName("dropOffAddress")]
        public string DropOffAddress { get; set; } = string.Empty;
        [JsonPropertyName("pickUpDate")]
        public string PickUpDate { get; set; } = string.Empty;
        [JsonPropertyName("pickUpTime")]
        public string PickUpTime { get; set; } = string.Empty;
        public double Price { get; set; }
        public string Comment { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        [JsonPropertyName("skynetStatus")]
        public string? SkynetStatus { get; set; }
        [JsonPropertyName("trackNo")]
        public string TrackNo { get; set; } = string.Empty;
        public string? Province { get; set; }
        [JsonPropertyName("destinationProvince")]
        public string? DestinationProvince { get; set; }
        public string Type { get; set; } = string.Empty;
        public int Distance { get; set; }
        [JsonPropertyName("dropOffPerson")]
        public DropOffPersonResponse DropOffPerson { get; set; } = new();
        [JsonPropertyName("pickUpPerson")]
        public PickUpPersonResponse PickUpPerson { get; set; } = new();
        [JsonPropertyName("waybillResponse")]
        public object? WaybillResponse { get; set; }
        [JsonPropertyName("fromSuburb")]
        public string FromSuburb { get; set; } = string.Empty;
        [JsonPropertyName("toSuburb")]
        public string ToSuburb { get; set; } = string.Empty;
        [JsonPropertyName("pickUpPCode")]
        public string PickUpPCode { get; set; } = string.Empty;
        [JsonPropertyName("dropOffPCode")]
        public string DropOffPCode { get; set; } = string.Empty;
        [JsonPropertyName("parcelDimensions")]
        public List<ParcelDimensionResponse> ParcelDimensions { get; set; } = new();
        [JsonPropertyName("isPaid")]
        public bool IsPaid { get; set; }
        [JsonPropertyName("receiverPaying")]
        public bool ReceiverPaying { get; set; }
        [JsonPropertyName("exportDetails")]
        public object? ExportDetails { get; set; }
        [JsonPropertyName("importDetails")]
        public object? ImportDetails { get; set; }
        [JsonPropertyName("promoCode")]
        public string? PromoCode { get; set; }
        [JsonPropertyName("promiseDateTimestamp")]
        public DateTime PromiseDateTimestamp { get; set; }
        [JsonPropertyName("cancellationReason")]
        public string? CancellationReason { get; set; }
        [JsonPropertyName("saIdNumber")]
        public string SaIdNumber { get; set; } = string.Empty;
        [JsonPropertyName("shipmentType")]
        public string ShipmentType { get; set; } = string.Empty;
        [JsonPropertyName("wooCommerceOrderId")]
        public string? WooCommerceOrderId { get; set; }
        [JsonPropertyName("toCounterCode")]
        public string ToCounterCode { get; set; } = string.Empty;
        [JsonPropertyName("toCounterName")]
        public string? ToCounterName { get; set; }
    }

    public class User
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("userType")]
        public string UserType { get; set; } = string.Empty;
        public Owner Owner { get; set; } = new();
        public object? Token { get; set; }
        public string Role { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
        [JsonPropertyName("accountNo")]
        public string AccountNo { get; set; } = string.Empty;
        [JsonPropertyName("canAddStickers")]
        public string CanAddStickers { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        [JsonPropertyName("vatDetails")]
        public object? VatDetails { get; set; }
    }

    public class DropOffPersonResponse
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;
        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        [JsonPropertyName("base64Image")]
        public string? Base64Image { get; set; }
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("companyName")]
        public string CompanyName { get; set; } = " ";
        public string Complex { get; set; } = string.Empty;
        [JsonPropertyName("unitNo")]
        public string UnitNo { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public string Suburb { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }

    public class PickUpPersonResponse
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;
        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        [JsonPropertyName("base64Image")]
        public string? Base64Image { get; set; }
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("companyName")]
        public string CompanyName { get; set; } = " ";
        public string Complex { get; set; } = string.Empty;
        [JsonPropertyName("unitNo")]
        public string UnitNo { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public string Suburb { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }

    public class ParcelDimensionResponse
    {
        [JsonPropertyName("parcel_length")]
        public double ParcelLength { get; set; }
        [JsonPropertyName("parcel_breadth")]
        public double ParcelBreadth { get; set; }
        [JsonPropertyName("parcel_height")]
        public double ParcelHeight { get; set; }
        [JsonPropertyName("parcel_mass")]
        public double ParcelMass { get; set; }
        [JsonPropertyName("parcel_reference")]
        public string ParcelReference { get; set; } = string.Empty;
        [JsonPropertyName("parcel_description")]
        public string? ParcelDescription { get; set; }
        [JsonPropertyName("parcel_trackNo")]
        public string? ParcelTrackNo { get; set; }
        [JsonPropertyName("predefinedParcel")]
        public string? PredefinedParcel { get; set; }
        [JsonPropertyName("selected_parcel")]
        public string? SelectedParcel { get; set; }
    }
}
