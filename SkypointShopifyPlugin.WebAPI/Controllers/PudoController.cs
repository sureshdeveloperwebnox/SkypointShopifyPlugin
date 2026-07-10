using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using Microsoft.Extensions.Options;
using System.Web;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/pudo")]
    public class PudoController : ControllerBase
    {
        private readonly ILogger<PudoController> _logger;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly ISkypointTokenStore _skypointTokenStore;
        private readonly SkypointApiSettings _apiSettings;
        private readonly IConfiguration _configuration;
        private readonly IConfigurationStore _configurationStore;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IShopTokenStore _shopTokenStore;

        public PudoController(
            ILogger<PudoController> logger,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore,
            IOptions<SkypointApiSettings> apiSettings,
            IConfiguration configuration,
            IConfigurationStore configurationStore,
            IShopifyAdminService shopifyAdminService,
            IShopTokenStore shopTokenStore)
        {
            _logger = logger;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
            _apiSettings = apiSettings.Value;
            _configuration = configuration;
            _configurationStore = configurationStore;
            _shopifyAdminService = shopifyAdminService;
            _shopTokenStore = shopTokenStore;
        }

        /// <summary>
        /// Generates the SkyPoint widget URL for PUDO point selection.
        /// GET /api/pudo/widget-url?shop=xxx&address=xxx&domain=xxx
        /// </summary>
        [HttpGet("widget-url")]
        public async Task<IActionResult> GetWidgetUrl([FromQuery] string shop, [FromQuery] string address, [FromQuery] string? domain)
        {
            if (string.IsNullOrEmpty(shop))
                return BadRequest(new { error = "shop domain is required" });

            var guid = Guid.NewGuid().ToString();
            
            string callbackDomain;
            if (!string.IsNullOrEmpty(domain))
            {
                callbackDomain = domain;
            }
            else if (!string.IsNullOrEmpty(shop))
            {
                callbackDomain = shop.StartsWith("http") ? shop : $"https://{shop}";
            }
            else
            {
                var callbackUrl = _configuration["Shopify:RedirectUri"] ?? $"{Request.Scheme}://{Request.Host}/api/shopify/auth";
                var uri = new Uri(callbackUrl);
                callbackDomain = $"{uri.Scheme}://{uri.Host}";
            }

            var appConfig = await _configurationStore.LoadConfigurationAsync();
            var widgetBaseUrl = appConfig?.SkypointApi?.PudoWidgetBaseUrl ?? _apiSettings.PudoWidgetBaseUrl;

            var widgetUrl = $"{widgetBaseUrl}?Guid={guid}&Location={HttpUtility.UrlEncode(address)}&Id=SKYONLINE&Env=TEST&Domain={HttpUtility.UrlEncode(callbackDomain)}";

            _logger.LogInformation("Generated PUDO widget URL for shop {Shop} with GUID {Guid} and Domain {Domain}", shop, guid, callbackDomain);

            return Ok(new
            {
                success = true,
                guid = guid,
                widget_url = widgetUrl
            });
        }

        /// <summary>
        /// Retrieves the selected PUDO point details using the session GUID.
        /// GET /api/pudo/selected/{guid}?shop=xxx
        /// </summary>
        [HttpGet("selected/{guid}")]
        public async Task<IActionResult> GetSelectedPoint(string guid, [FromQuery] string shop)
        {
            if (string.IsNullOrEmpty(shop))
                return BadRequest(new { error = "shop domain is required" });

            try
            {
                var token = await GetOrRefreshTokenAsync(shop);
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogError("SkyPoint token not configured or expired for shop {Shop}", shop);
                    return BadRequest(new { error = "SkyPoint integration not configured or credentials expired for this store" });
                }

                var response = await _skypointApiClient.GetSelectedPudoPointAsync(guid, token);
                if (response == null)
                {
                    return Ok(new { success = false, message = "PUDO point selection not found or not selected yet" });
                }

                return Ok(new { success = true, pudo_point = response });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching selected PUDO point for GUID {Guid} and shop {Shop}", guid, shop);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Creates a Shopify checkout with the selected PUDO address pre-filled.
        /// POST /api/pudo/checkout-url
        /// Body: { shop, address1, address2, city, zip, country, first_name, last_name }
        /// Returns: { success, checkout_url }
        /// </summary>
        [HttpPost("checkout-url")]
        public async Task<IActionResult> CreateCheckoutUrl([FromBody] CreateCheckoutRequest req)
        {
            if (string.IsNullOrEmpty(req?.Shop))
                return BadRequest(new { error = "shop is required" });

            var accessToken = _shopTokenStore.GetToken(req.Shop);
            if (string.IsNullOrEmpty(accessToken))
                return BadRequest(new { error = "Shop not installed or token missing" });

            var items = req.LineItems != null
                ? req.LineItems.Select(i => new CheckoutLineItemDto(i.VariantId, i.Quantity))
                : Array.Empty<CheckoutLineItemDto>();

            var checkoutUrl = await _shopifyAdminService.CreateCheckoutWithAddressAsync(
                shopDomain:  req.Shop,
                accessToken: accessToken,
                address1:    req.Address1 ?? "",
                address2:    req.Address2 ?? "",
                city:        req.City     ?? "",
                zip:         req.Zip      ?? "",
                countryCode: req.Country  ?? "ZA",
                firstName:   req.FirstName,
                lastName:    req.LastName,
                lineItems:   items,
                pudoCode:    req.PudoCode,
                pudoName:    req.PudoName,
                pudoProvider: req.PudoProvider);

            if (string.IsNullOrEmpty(checkoutUrl))
                return StatusCode(500, new { error = "Failed to create checkout with address" });

            return Ok(new { success = true, checkout_url = checkoutUrl });
        }

        public record CreateCheckoutRequest(
            string? Shop,
            string? Address1,
            string? Address2,
            string? City,
            string? Zip,
            string? Country,
            string? FirstName,
            string? LastName,
            List<CheckoutLineItem>? LineItems,
            string? PudoCode = null,
            string? PudoName = null,
            string? PudoProvider = null);

        public record CheckoutLineItem(long VariantId, int Quantity);


        private async Task<string?> GetOrRefreshTokenAsync(string? shopDomain)
        {
            if (string.IsNullOrEmpty(shopDomain)) return null;

            // 1. Valid cached token
            var cached = _skypointTokenStore.GetToken(shopDomain);
            if (!string.IsNullOrEmpty(cached)) return cached;

            // 2. Token expired — re-login using stored credentials
            var creds = _skypointTokenStore.GetCredentials(shopDomain);
            if (creds == null)
            {
                _logger.LogWarning("No credentials stored for shop {Shop} when requesting PUDO details.", shopDomain);
                return null;
            }

            try
            {
                _logger.LogInformation("Refreshing Skypoint token for PUDO request for shop {Shop}", shopDomain);
                var loginResponse = await _skypointApiClient.LoginAsync(new LoginRequest
                {
                    Username = creds.Value.username,
                    Pwd = creds.Value.password
                });

                if (loginResponse?.Token?.TokenValue != null)
                {
                    _skypointTokenStore.SaveToken(shopDomain, loginResponse.Token.TokenValue, loginResponse.Token.Expiration, loginResponse.Id);
                    _logger.LogInformation("Token refreshed for shop {Shop} for PUDO request", shopDomain);
                    return loginResponse.Token.TokenValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh Skypoint token for PUDO request for shop {Shop}", shopDomain);
            }

            return null;
        }
    }
}
