namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class BookingResponse
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        public User User { get; set; } = new();
        public string PickUpAddress { get; set; } = string.Empty;
        public string DropOffAddress { get; set; } = string.Empty;
        public string PickUpDate { get; set; } = string.Empty;
        public string PickUpTime { get; set; } = string.Empty;
        public double Price { get; set; }
        public string Comment { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? SkynetStatus { get; set; }
        public string TrackNo { get; set; } = string.Empty;
        public string? Province { get; set; }
        public string? DestinationProvince { get; set; }
        public string Type { get; set; } = string.Empty;
        public int Distance { get; set; }
        public DropOffPersonResponse DropOffPerson { get; set; } = new();
        public PickUpPersonResponse PickUpPerson { get; set; } = new();
        public object? WaybillResponse { get; set; }
        public string FromSuburb { get; set; } = string.Empty;
        public string ToSuburb { get; set; } = string.Empty;
        public string PickUpPCode { get; set; } = string.Empty;
        public string DropOffPCode { get; set; } = string.Empty;
        public List<ParcelDimensionResponse> ParcelDimensions { get; set; } = new();
        public bool IsPaid { get; set; }
        public bool ReceiverPaying { get; set; }
        public object? ExportDetails { get; set; }
        public object? ImportDetails { get; set; }
        public string? PromoCode { get; set; }
        public DateTime PromiseDateTimestamp { get; set; }
        public string? CancellationReason { get; set; }
        public string SaIdNumber { get; set; } = string.Empty;
        public string ShipmentType { get; set; } = string.Empty;
        public string? WooCommerceOrderId { get; set; }
        public string ToCounterCode { get; set; } = string.Empty;
        public string? ToCounterName { get; set; }
    }

    public class User
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        public string Username { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public Owner Owner { get; set; } = new();
        public object? Token { get; set; }
        public string Role { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
        public string AccountNo { get; set; } = string.Empty;
        public string CanAddStickers { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public object? VatDetails { get; set; }
    }

    public class DropOffPersonResponse
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Base64Image { get; set; }
        public string Email { get; set; } = string.Empty;
        public string CompanyName { get; set; } = " ";
        public string Complex { get; set; } = string.Empty;
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
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Base64Image { get; set; }
        public string Email { get; set; } = string.Empty;
        public string CompanyName { get; set; } = " ";
        public string Complex { get; set; } = string.Empty;
        public string UnitNo { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public string Suburb { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }

    public class ParcelDimensionResponse
    {
        public double ParcelLength { get; set; }
        public double ParcelBreadth { get; set; }
        public double ParcelHeight { get; set; }
        public double ParcelMass { get; set; }
        public string ParcelReference { get; set; } = string.Empty;
        public string? ParcelDescription { get; set; }
        public string? ParcelTrackNo { get; set; }
        public string? PredefinedParcel { get; set; }
        public string? SelectedParcel { get; set; }
    }
}
