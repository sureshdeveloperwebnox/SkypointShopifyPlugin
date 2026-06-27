namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface IShopifyOAuthService
    {
        string GetInstallUrl(string shop);
        Task<string> ExchangeCodeForAccessTokenAsync(string shop, string code);
        bool VerifyWebhookSignature(string body, string signature, string webhookSecret);
    }
}
