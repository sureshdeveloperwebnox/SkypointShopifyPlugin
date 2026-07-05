using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Core.Configuration;
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

        public PudoController(
            ILogger<PudoController> logger,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore,
            IOptions<SkypointApiSettings> apiSettings,
            IConfiguration configuration,
            IConfigurationStore configurationStore)
        {
            _logger = logger;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
            _apiSettings = apiSettings.Value;
            _configuration = configuration;
            _configurationStore = configurationStore;
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
                var token = _skypointTokenStore.GetToken(shop);
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogError("SkyPoint token not configured for shop {Shop}", shop);
                    return BadRequest(new { error = "SkyPoint integration not configured for this store" });
                }

                var response = await _skypointApiClient.GetSelectedPudoPointAsync(guid, token);
                if (response == null)
                {
                    return NotFound(new { error = "PUDO point selection not found or expired" });
                }

                return Ok(new { success = true, pudo_point = response });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching selected PUDO point for GUID {Guid} and shop {Shop}", guid, shop);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }
    }
}
