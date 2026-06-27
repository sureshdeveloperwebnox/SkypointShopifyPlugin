namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class RateRequest
    {
        public string PickUpSuburb { get; set; } = string.Empty;
        public string PickUpPostalCode { get; set; } = string.Empty;
        public string DropOffSuburb { get; set; } = string.Empty;
        public string DropOverPostalCode { get; set; } = string.Empty;
        public List<ParcelDimension> ParcelsDims { get; set; } = new();
    }

    public class ParcelDimension
    {
        public double ParcelMass { get; set; }
        public double ParcelLength { get; set; }
        public double ParcelBreadth { get; set; }
        public double ParcelHeight { get; set; }
        public string PredefinedParcel { get; set; } = string.Empty;
        public string ParcelReference { get; set; } = string.Empty;
        public string SelectedParcel { get; set; } = string.Empty;
    }
}
