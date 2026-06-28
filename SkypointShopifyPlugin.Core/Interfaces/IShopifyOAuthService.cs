namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface IShopifyOAuthService
    {
        string GetInstallUrl(string shop, string redirectUri);
        Task<string> ExchangeCodeForAccessTokenAsync(string shop, string code);
        Task<string?> GetTokenViaClientCredentialsAsync(string shop);
        bool VerifyWebhookSignature(string body, string signature, string webhookSecret);
    }
}
