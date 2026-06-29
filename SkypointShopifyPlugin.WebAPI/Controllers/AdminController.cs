using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly ILogger<AdminController> _logger;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IConfiguration _configuration;
        private readonly IShopTokenStore _shopTokenStore;
        private readonly IShopifyOAuthService _oauthService;

        public AdminController(ILogger<AdminController> logger, IShopifyAdminService shopifyAdminService, IConfiguration configuration, IShopTokenStore shopTokenStore, IShopifyOAuthService oauthService)
        {
            _logger = logger;
            _shopifyAdminService = shopifyAdminService;
            _configuration = configuration;
            _shopTokenStore = shopTokenStore;
            _oauthService = oauthService;
        }

        /// <summary>
        /// Register carrier service using stored token (call after OAuth install)
        /// </summary>
        [HttpPost("register-carrier")]
        public async Task<IActionResult> RegisterCarrierService([FromBody] CarrierRegistrationRequest request)
        {
            return await DoRegister(request.Shop, request.AccessToken);
        }

        /// <summary>
        /// GET version — trigger from browser: /api/admin/register-carrier?shop=x&accessToken=y
        /// </summary>
        [HttpGet("register-carrier")]
        public async Task<IActionResult> RegisterCarrierServiceGet([FromQuery] string shop, [FromQuery] string? accessToken)
        {
            return await DoRegister(shop, accessToken ?? string.Empty);
        }

        [HttpPost("sync-webhooks")]
        public async Task<IActionResult> SyncWebhooks([FromBody] WebhookSyncRequest request)
        {
            return await DoSyncWebhooks(request.Shop, request.AccessToken);
        }

        [HttpGet("sync-webhooks")]
        public async Task<IActionResult> SyncWebhooksGet([FromQuery] string shop, [FromQuery] string? accessToken)
        {
            return await DoSyncWebhooks(shop, accessToken ?? string.Empty);
        }

        private async Task<IActionResult> DoRegister(string shop, string accessToken)
        {
            _logger.LogInformation("Carrier service registration request for shop: {Shop}", shop);

            try
            {
                if (string.IsNullOrEmpty(shop))
                    return BadRequest(new { error = "Shop domain is required" });

                // Use provided token, or fetch dynamically via client_credentials
                if (string.IsNullOrEmpty(accessToken))
                    accessToken = _shopTokenStore.GetToken(shop) ?? string.Empty;

                if (string.IsNullOrEmpty(accessToken))
                {
                    accessToken = await _oauthService.GetTokenViaClientCredentialsAsync(shop) ?? string.Empty;
                    if (!string.IsNullOrEmpty(accessToken))
                        _shopTokenStore.SaveToken(shop, accessToken);
                }

                if (string.IsNullOrEmpty(accessToken))
                    return BadRequest(new { error = "Could not obtain access token. Ensure the app is installed on this store." });

                // Save it so future calls don't need the token
                _shopTokenStore.SaveToken(shop, accessToken);

                var carrierServiceUrl = $"{Request.Scheme}://{Request.Host}/api/carrier/rates?shop={Uri.EscapeDataString(shop)}";
                var (registered, message) = await _shopifyAdminService.RegisterAndAssignCarrierServiceAsync(shop, accessToken, carrierServiceUrl);

                if (registered)
                    return Ok(new { message, shop, carrier_service_url = carrierServiceUrl });
                else
                    return StatusCode(500, new { error = message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering carrier service for shop: {Shop}", shop);
                return StatusCode(500, new { error = "Internal server error: " + ex.Message });
            }
        }

        private async Task<IActionResult> DoSyncWebhooks(string shop, string accessToken)
        {
            _logger.LogInformation("Webhook sync request for shop: {Shop}", shop);

            try
            {
                if (string.IsNullOrEmpty(shop))
                    return BadRequest(new { error = "Shop domain is required" });

                shop = shop.Replace("https://", "").Replace("http://", "").TrimEnd('/');

                if (string.IsNullOrEmpty(accessToken))
                    accessToken = _shopTokenStore.GetToken(shop) ?? string.Empty;

                if (string.IsNullOrEmpty(accessToken))
                {
                    accessToken = await _oauthService.GetTokenViaClientCredentialsAsync(shop) ?? string.Empty;
                    if (!string.IsNullOrEmpty(accessToken))
                        _shopTokenStore.SaveToken(shop, accessToken);
                }

                if (string.IsNullOrEmpty(accessToken))
                    return BadRequest(new { error = "Could not obtain access token. Reinstall the app on this store." });

                _shopTokenStore.SaveToken(shop, accessToken);

                var publicBaseUrl = $"{Request.Scheme}://{Request.Host}";
                var (success, message) = await _shopifyAdminService.SyncWebhooksAsync(shop, accessToken, publicBaseUrl);

                if (success)
                    return Ok(new { message, shop, public_base_url = publicBaseUrl });

                return StatusCode(500, new { error = message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing webhooks for shop: {Shop}", shop);
                return StatusCode(500, new { error = "Internal server error: " + ex.Message });
            }
        }
    }

    public class CarrierRegistrationRequest
    {
        public string Shop { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
    }

    public class WebhookSyncRequest
    {
        public string Shop { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
    }
}
