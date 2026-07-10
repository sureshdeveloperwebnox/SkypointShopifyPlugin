namespace SkypointShopifyPlugin.Core.Configuration
{
    public class SkypointShopifySettings
    {
        // API Credentials
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsUat { get; set; } = true;

        // Shipper/Origin Pick Up Details
        public string ShipperCompanyName { get; set; } = string.Empty;
        public string ShipperFirstName { get; set; } = string.Empty;
        public string ShipperLastName { get; set; } = string.Empty;
        public string ShipperEmail { get; set; } = string.Empty;
        public string ShipperPhone { get; set; } = string.Empty;
        public string ShipperAddress1 { get; set; } = string.Empty;
        public string ShipperSuburb { get; set; } = string.Empty;
        public string ShipperPostcode { get; set; } = string.Empty;
        public string ShipperCity { get; set; } = string.Empty;
        public string ShipperProvince { get; set; } = string.Empty;

        // Rules & Defaults
        public decimal FallbackCost { get; set; } = 0;
        public decimal FreeshipThreshold { get; set; } = 0;
        public double DefaultMass { get; set; } = 0.5;
        public double DefaultLength { get; set; } = 10.0;
        public double DefaultBreadth { get; set; } = 10.0;
        public double DefaultHeight { get; set; } = 10.0;

        // Shipping Mode Toggles
        public bool EnableRoad { get; set; } = true;
        public bool EnableAir { get; set; } = true;
        public bool EnableCounter { get; set; } = true;

        // Renaming Options
        public string RenameRoad { get; set; } = "EXPRESS ROAD";
        public string RenameAir { get; set; } = "EXPRESS AIR";
        public string RenameCounter { get; set; } = "DOOR TO COUNTER";

        // Display Ordering
        public int SortRoad { get; set; } = 1;
        public int SortAir { get; set; } = 2;
        public int SortCounter { get; set; } = 3;

        // Advanced Markup Customization
        public string MarkupType { get; set; } = "percent"; // "percent" or "flat"
        public decimal MarkupRoad { get; set; } = 0;
        public decimal MarkupAir { get; set; } = 0;
        public decimal MarkupCounter { get; set; } = 0;
    }
}
