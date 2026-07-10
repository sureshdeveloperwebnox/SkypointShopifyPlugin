using System.Threading.Tasks;
using SkypointShopifyPlugin.Core.Configuration;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface ISkypointSettingsService
    {
        Task<SkypointShopifySettings> GetSettingsAsync(string shopDomain);
    }
}
