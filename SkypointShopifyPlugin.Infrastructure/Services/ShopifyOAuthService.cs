using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        public ShopifyOAuthService(IOptions<ShopifySettings> settings, HttpClient httpClient, ILogger<ShopifyOAuthService> logger)
        {
            _settings = settings.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        public string GetInstallUrl(string shop, string redirectUri)
        {
            if (!IsValidShopDomain(shop))
                throw new ArgumentException("Invalid shop domain");

            var scopes = _settings.Scopes;
            var state = GenerateState();
            var installUrl = $"https://{shop}/admin/oauth/authorize?client_id={_settings.ClientId}&scope={scopes}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={state}";
            _logger.LogInformation("Generated install URL for shop: {Shop}", shop);
            return installUrl;
        }

        public async Task<ShopifyTokenResponse> ExchangeCodeForAccessTokenAsync(string shop, string code)
        {
            var tokenUrl = $"https://{shop}/admin/oauth/access_token";
            var requestBody = new { client_id = _settings.ClientId, client_secret = _settings.ClientSecret, code = code, expiring = 1 };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(tokenUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Token exchange failed {response.StatusCode}: {responseBody}");
            var tokenResponse = JsonSerializer.Deserialize<ShopifyTokenResponse>(responseBody);
            if (tokenResponse?.access_token == null)
                throw new Exception($"No access_token in response: {responseBody}");
            _logger.LogInformation("Access token obtained for shop: {Shop}", shop);
            return tokenResponse;
        }

        public async Task<string?> GetTokenViaClientCredentialsAsync(string shop)
        {
            // Try all known app credentials — handles stores installed with legacy app IDs
            foreach (var (clientId, clientSecret) in _settings.GetAllCredentials())
            {
                try
                {
                    var tokenUrl = $"https://{shop}/admin/oauth/access_token";
                    var requestBody = new
                    {
                        client_id = clientId,
                        client_secret = clientSecret,
                        grant_type = "client_credentials"
                    };
                    var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(tokenUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        var tokenResponse = JsonSerializer.Deserialize<ShopifyTokenResponse>(body);
                        if (!string.IsNullOrEmpty(tokenResponse?.access_token))
                        {
                            _logger.LogInformation("Got token via client_credentials for {Shop} using client_id={ClientId}", shop, clientId[..8]);
                            return tokenResponse.access_token;
                        }
                    }
                    else
                    {
                        var err = await response.Content.ReadAsStringAsync();
                        _logger.LogDebug("client_credentials failed for {Shop} with client_id={ClientId}: {Status}", shop, clientId[..8], response.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Exception trying client_credentials for {Shop} with client_id={ClientId}", shop, clientId[..8]);
                }
            }

            _logger.LogWarning("All client_credentials attempts failed for {Shop}", shop);
            return null;
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
