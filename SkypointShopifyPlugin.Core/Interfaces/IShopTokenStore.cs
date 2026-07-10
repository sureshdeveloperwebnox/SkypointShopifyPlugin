namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface IShopTokenStore
    {
        void SaveToken(string shopDomain, string accessToken);
        void SaveToken(string shopDomain, string accessToken, string? refreshToken, int? expiresInSeconds);
        string? GetToken(string shopDomain);
        bool HasToken(string shopDomain);
        void RemoveToken(string shopDomain);

        /// <summary>Returns all shop domains that currently have a stored Shopify OAuth token.</summary>
        IReadOnlyList<string> GetAllShops();
    }
}
