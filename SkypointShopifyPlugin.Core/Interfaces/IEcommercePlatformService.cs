using System.Threading.Tasks;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface IEcommercePlatformService
    {
        Task<bool> UpdateTrackingAsync(string shopDomain, string orderId, string trackNo, string carrierName, string trackingUrl, string status);
    }
}
