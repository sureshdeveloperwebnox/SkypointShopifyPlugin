using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/shipping")]
    public class ShippingController : ControllerBase
    {
        private readonly ILogger<ShippingController> _logger;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly IConfiguration _configuration;

        public ShippingController(ILogger<ShippingController> logger, ISkypointApiClient skypointApiClient, IConfiguration configuration)
        {
            _logger = logger;
            _skypointApiClient = skypointApiClient;
            _configuration = configuration;
        }

        /// <summary>
        /// Get shipping rates - can be called from Shopify checkout or manually
        /// </summary>
        [HttpPost("rates")]
        public async Task<IActionResult> GetShippingRates([FromBody] ShippingRateRequest request)
        {
            _logger.LogInformation("Shipping rate request for pickup: {PickupCity}, delivery: {DeliveryCity}", 
                request.PickupCity, request.DeliveryCity);

            try
            {
                // Get Skypoint credentials from configuration
                var username = _configuration["Skypoint:Username"];
                var password = _configuration["Skypoint:Password"];

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    _logger.LogError("Skypoint credentials not configured");
                    return BadRequest(new { error = "Skypoint credentials not configured" });
                }

                // Login to Skypoint
                var loginResponse = await _skypointApiClient.LoginAsync(new LoginRequest
                {
                    Username = username,
                    Pwd = password
                });

                if (loginResponse == null || string.IsNullOrEmpty(loginResponse.Token?.TokenValue))
                {
                    _logger.LogError("Failed to login to Skypoint API");
                    return BadRequest(new { error = "Failed to login to Skypoint API" });
                }

                // Map request to Skypoint rate request
                var skypointRateRequest = new RateRequest
                {
                    PickUpSuburb = request.PickupCity,
                    PickUpPostalCode = request.PickupPostalCode,
                    DropOffSuburb = request.DeliveryCity,
                    DropOffPostalCode = request.DeliveryPostalCode,
                    ParcelsDims = request.ParcelDimensions.Select(pd => new ParcelDimension
                    {
                        ParcelMass = pd.Weight,
                        ParcelLength = pd.Length,
                        ParcelBreadth = pd.Width,
                        ParcelHeight = pd.Height,
                        ParcelReference = pd.Reference
                    }).ToList()
                };

                // Get rates from Skypoint
                var skypointRates = await _skypointApiClient.GetRatesAsync(skypointRateRequest, loginResponse.Token.TokenValue);

                // Map to response format
                var rates = skypointRates.Select(rate => new ShippingRateResponse
                {
                    ServiceName = rate.ServiceName,
                    Description = rate.ServiceDescription,
                    Price = rate.Price,
                    TransitDays = rate.TransitDays,
                    Currency = request.Currency ?? "ZAR"
                }).ToList();

                _logger.LogInformation("Returning {Count} shipping rates", rates.Count);
                return Ok(rates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shipping rates");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    public class ShippingRateRequest
    {
        public string PickupCity { get; set; } = string.Empty;
        public string PickupPostalCode { get; set; } = string.Empty;
        public string DeliveryCity { get; set; } = string.Empty;
        public string DeliveryPostalCode { get; set; } = string.Empty;
        public string Currency { get; set; } = "ZAR";
        public List<ParcelDimensionRequest> ParcelDimensions { get; set; } = new();
    }

    public class ParcelDimensionRequest
    {
        public double Weight { get; set; }
        public double Length { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Reference { get; set; } = string.Empty;
    }

    public class ShippingRateResponse
    {
        public string ServiceName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Price { get; set; }
        public int TransitDays { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}
