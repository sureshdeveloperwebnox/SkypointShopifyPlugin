using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class ShopifyOAuthService : IShopifyOAuthService
    {
        private readonly ShopifySettings _settings;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ShopifyOAuthService> _logger;

        public ShopifyOAuthService(ShopifySettings settings, HttpClient httpClient, ILogger<ShopifyOAuthService> logger)
        {
            _settings = settings;
            _httpClient = httpClient;
            _logger = logger;
        }

        public string GetInstallUrl(string shop)
        {
            // Validate shop format
            if (!IsValidShopDomain(shop))
            {
                throw new ArgumentException("Invalid shop domain");
            }

            var scopes = _settings.Scopes;
            var redirectUri = Uri.EscapeDataString(_settings.RedirectUri);
            var clientId = _settings.ClientId;
            var state = GenerateState();

            var installUrl = $"https://{shop}/admin/oauth/authorize?client_id={clientId}&scope={scopes}&redirect_uri={redirectUri}&state={state}";

            _logger.LogInformation("Generated install URL for shop: {Shop}", shop);
            return installUrl;
        }

        public async Task<string> ExchangeCodeForAccessTokenAsync(string shop, string code)
        {
            var tokenUrl = $"https://{shop}/admin/oauth/access_token";
            
            var requestBody = new
            {
                client_id = _settings.ClientId,
                client_secret = _settings.ClientSecret,
                code = code
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(tokenUrl, content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<ShopifyTokenResponse>(responseBody);

            if (tokenResponse?.access_token == null)
            {
                throw new Exception("Failed to obtain access token");
            }

            _logger.LogInformation("Successfully obtained access token for shop: {Shop}", shop);
            return tokenResponse.access_token;
        }

        public bool VerifyWebhookSignature(string body, string signature, string webhookSecret)
        {
            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(webhookSecret))
            {
                return false;
            }

            var secretBytes = Encoding.UTF8.GetBytes(webhookSecret);
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            using var hmac = new HMACSHA256(secretBytes);
            var hash = hmac.ComputeHash(bodyBytes);
            var computedSignature = Convert.ToHexString(hash).ToLowerInvariant();

            // Shopify sends signature in base64 format
            var signatureBytes = Convert.FromBase64String(signature);
            var computedSignatureBytes = Convert.FromHexString(computedSignature);

            return CryptographicOperations.FixedTimeEquals(signatureBytes, computedSignatureBytes);
        }

        private bool IsValidShopDomain(string shop)
        {
            if (string.IsNullOrEmpty(shop))
                return false;

            // Remove protocol if present
            shop = shop.Replace("https://", "").Replace("http://", "");

            // Basic validation: ends with .myshopify.com
            return shop.EndsWith(".myshopify.com", StringComparison.OrdinalIgnoreCase);
        }

        private string GenerateState()
        {
            var randomBytes = new byte[32];
            RandomNumberGenerator.Fill(randomBytes);
            return Convert.ToHexString(randomBytes).ToLowerInvariant();
        }
    }
}
