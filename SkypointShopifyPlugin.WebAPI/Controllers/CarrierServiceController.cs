using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/carrier")]
    public class CarrierServiceController : ControllerBase
    {
        private readonly ILogger<CarrierServiceController> _logger;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly ISkypointTokenStore _skypointTokenStore;

        public CarrierServiceController(
            ILogger<CarrierServiceController> logger,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore)
        {
            _logger = logger;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
        }

        /// <summary>
        /// Shopify calls this at checkout to get live shipping rates.
        /// Shop is identified from:
        ///   1. ?shop= query param on the callback URL (set during carrier registration)
        ///   2. X-Shopify-Shop-Domain header (sent by Shopify)
        /// Token is fetched from memory cache, auto-refreshed using stored credentials.
        /// No hardcoded credentials, no file storage.
        /// </summary>
        [HttpPost("rates")]
        public async Task<IActionResult> GetRates([FromBody] CarrierServiceRequest request)
        {
            _logger.LogInformation("Rate request: {Origin} → {Dest}",
                request.Origin.City, request.Destination.City);

            try
            {
                // Resolve shop domain from URL param first, then headers
                var shopDomain = Request.Query["shop"].ToString();
                if (string.IsNullOrEmpty(shopDomain))
                    shopDomain = Request.Headers["X-Shopify-Shop-Domain"].ToString();
                if (string.IsNullOrEmpty(shopDomain))
                    shopDomain = Request.Headers["X-Shop-Domain"].ToString();

                shopDomain = shopDomain?.Replace("https://", "").Replace("http://", "").TrimEnd('/');

                _logger.LogInformation("Rate request from shop: {Shop}", 
                    string.IsNullOrEmpty(shopDomain) ? "(unknown)" : shopDomain);

                // Get token — try cache first, then auto-refresh using stored credentials
                var skypointToken = await GetOrRefreshTokenAsync(shopDomain);

                if (string.IsNullOrEmpty(skypointToken))
                {
                    _logger.LogWarning("No Skypoint token available for shop '{Shop}'. Merchant must log in to the app.", shopDomain);
                    return Ok(new CarrierServiceResponse { Rates = new List<ShippingRate>() });
                }

                // Build Skypoint rate request
                var rateRequest = new RateRequest
                {
                    PickUpSuburb = request.Origin.City,
                    PickUpPostalCode = request.Origin.PostalCode,
                    DropOffSuburb = request.Destination.City,
                    DropOverPostalCode = request.Destination.PostalCode,
                    ParcelsDims = request.Items.Select(item => new ParcelDimension
                    {
                        ParcelMass = item.Grams > 0 ? item.Grams / 1000.0 : 0.5,
                        ParcelLength = 10.0,
                        ParcelBreadth = 10.0,
                        ParcelHeight = 10.0,
                        PredefinedParcel = "A4_Text_Book",
                        ParcelReference = !string.IsNullOrEmpty(item.Sku) ? item.Sku : item.Name,
                        SelectedParcel = "A4_Text_Book"
                    }).ToList()
                };

                // Fallback: if no items, add a default parcel so Skypoint doesn't reject
                if (!rateRequest.ParcelsDims.Any())
                {
                    rateRequest.ParcelsDims.Add(new ParcelDimension
                    {
                        ParcelMass = 0.5,
                        ParcelLength = 10.0, ParcelBreadth = 10.0, ParcelHeight = 10.0,
                        PredefinedParcel = "A4_Text_Book", SelectedParcel = "A4_Text_Book",
                        ParcelReference = "DEFAULT"
                    });
                }

                _logger.LogInformation("Requesting rates: {Origin} → {Dest}, {Count} items",
                    rateRequest.PickUpSuburb, rateRequest.DropOffSuburb, rateRequest.ParcelsDims.Count);

                var skypointRates = await _skypointApiClient.GetRatesAsync(rateRequest, skypointToken);

                var shopifyRates = new CarrierServiceResponse
                {
                    Rates = skypointRates.Select(rate => new ShippingRate
                    {
                        ServiceName = $"Skypoint {rate.ServiceName}",
                        ServiceCode = rate.ServiceName,
                        TotalPrice = (int)Math.Round(rate.Price * 100),
                        Description = rate.ServiceDescription,
                        Currency = request.CurrencyCode,
                        MinDeliveryDate = DateTime.UtcNow.AddDays(rate.TransitDays).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        MaxDeliveryDate = DateTime.UtcNow.AddDays(rate.TransitDays + 2).ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }).ToList()
                };

                _logger.LogInformation("Returning {Count} rates for shop {Shop}",
                    shopifyRates.Rates.Count, shopDomain);
                return Ok(shopifyRates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shipping rates");
                return Ok(new CarrierServiceResponse { Rates = new List<ShippingRate>() });
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────

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
                _logger.LogWarning("No credentials stored for shop {Shop}", shopDomain);
                return null;
            }

            try
            {
                _logger.LogInformation("Refreshing Skypoint token for shop {Shop}", shopDomain);
                var loginResponse = await _skypointApiClient.LoginAsync(new LoginRequest
                {
                    Username = creds.Value.username,
                    Pwd = creds.Value.password
                });

                if (loginResponse?.Token?.TokenValue != null)
                {
                    _skypointTokenStore.SaveToken(shopDomain, loginResponse.Token.TokenValue, loginResponse.Token.Expiration);
                    _logger.LogInformation("Token refreshed for shop {Shop}", shopDomain);
                    return loginResponse.Token.TokenValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh Skypoint token for shop {Shop}", shopDomain);
            }

            return null;
        }
    }
}
