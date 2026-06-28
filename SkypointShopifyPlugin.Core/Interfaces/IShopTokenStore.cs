namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface IShopTokenStore
    {
        void SaveToken(string shopDomain, string accessToken);
        string? GetToken(string shopDomain);
        bool HasToken(string shopDomain);
        void RemoveToken(string shopDomain);
    }
}
