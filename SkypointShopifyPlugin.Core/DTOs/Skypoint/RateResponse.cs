namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class RateResponse
    {
        public string ServiceName { get; set; } = string.Empty;
        public string ServiceDescription { get; set; } = string.Empty;
        public string? FromCityArea { get; set; }
        public string? ToCityArea { get; set; }
        public double Price { get; set; }
        public double FreightCharge { get; set; }
        public bool IsHighRiskPickUp { get; set; }
        public bool IsRemotePickUp { get; set; }
        public bool IsHighRiskDropOff { get; set; }
        public bool IsRemoteDropOff { get; set; }
        public List<string>? CollectionServiceDays { get; set; }
        public List<string>? DeliveryServiceDays { get; set; }
        public int TransitDays { get; set; }
        public double VolumeMass { get; set; }
        public double ActualMass { get; set; }
        public double ChargedMass { get; set; }
    }
}
