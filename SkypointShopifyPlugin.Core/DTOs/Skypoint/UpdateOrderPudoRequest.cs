namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class UpdateOrderPudoRequest
    {
        public string ToCounterCode { get; set; } = string.Empty;
        public string ToCounterName { get; set; } = string.Empty;
        public string PudoAddress1 { get; set; } = string.Empty;
        public string PudoCity { get; set; } = string.Empty;
        public string PudoZip { get; set; } = string.Empty;
        public string PudoProvider { get; set; } = string.Empty;
    }
}
