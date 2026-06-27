namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class BookingRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string PickUpAddress { get; set; } = string.Empty;
        public string DropOffAddress { get; set; } = string.Empty;
        public string FromSuburb { get; set; } = string.Empty;
        public string ToSuburb { get; set; } = string.Empty;
        public string PickUpPCode { get; set; } = string.Empty;
        public string DropOffPCode { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string DestinationProvince { get; set; } = string.Empty;
        public DropOffPerson DropOff { get; set; } = new();
        public PickUpPerson PickUp { get; set; } = new();
        public string Type { get; set; } = "ROAD";
        public string PickUpDate { get; set; } = string.Empty;
        public string PickUpTime { get; set; } = string.Empty;
        public List<ParcelDimension> ParcelDimensions { get; set; } = new();
        public string PickUpCity { get; set; } = string.Empty;
        public string DropOffCity { get; set; } = string.Empty;
        public string PickUpZip { get; set; } = string.Empty;
        public string DropOffZip { get; set; } = string.Empty;
        public string ShipmentType { get; set; } = string.Empty;
        public string ToCounterCode { get; set; } = string.Empty;
        public string ToCounterName { get; set; } = string.Empty;
        public string SaIdNumber { get; set; } = string.Empty;
        public string PickUpCountry { get; set; } = string.Empty;
    }

    public class DropOffPerson
    {
        public string Base64Image { get; set; } = ".";
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CompanyName { get; set; } = " ";
        public string Complex { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public string UnitNo { get; set; } = string.Empty;
        public string Suburb { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }

    public class PickUpPerson
    {
        public string Base64Image { get; set; } = ".";
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CompanyName { get; set; } = " ";
        public string Complex { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public string UnitNo { get; set; } = string.Empty;
        public string Suburb { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }
}
