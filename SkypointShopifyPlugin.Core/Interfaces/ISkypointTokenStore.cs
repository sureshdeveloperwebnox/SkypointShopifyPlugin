namespace SkypointShopifyPlugin.Core.Interfaces
{
    /// <summary>
    /// Stores Skypoint credentials and cached tokens per shop.
    /// Credentials are set when the merchant logs in.
    /// Tokens are cached in memory and refreshed automatically — nothing written to disk.
    /// </summary>
    public interface ISkypointTokenStore
    {
        // Store credentials when merchant logs in
        void SaveCredentials(string shopDomain, string username, string password);

        // Cache a token (after successful login)
        void SaveToken(string shopDomain, string skypointToken, DateTime expiration);

        // Get valid cached token, or null if expired/missing
        string? GetToken(string shopDomain);

        // Get stored credentials for re-login when token expires
        (string username, string password)? GetCredentials(string shopDomain);

        bool IsTokenValid(string shopDomain);
        void Clear(string shopDomain);
    }
}
