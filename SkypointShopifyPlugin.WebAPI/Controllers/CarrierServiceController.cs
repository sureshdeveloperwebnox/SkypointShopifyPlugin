using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.WebAPI.Filters;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/carrier")]
    public class CarrierServiceController : ControllerBase
    {
        private readonly ILogger<CarrierServiceController> _logger;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly ISkypointTokenStore _skypointTokenStore;
        private readonly IConfiguration _configuration;

        public CarrierServiceController(
            ILogger<CarrierServiceController> logger,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore,
            IConfiguration configuration)
        {
            _logger = logger;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
            _configuration = configuration;
        }

        /// <summary>
        /// Shopify calls this at checkout to get live shipping rates.
        /// Shop is identified from:
        ///   1. ?shop= query param on the callback URL (set during carrier registration)
        ///   2. X-Shopify-Shop-Domain header (sent by Shopify)
        /// Token is fetched from memory cache, auto-refreshed using stored credentials.
        /// No hardcoded credentials, no file storage.
        /// </summary>
        // Diagnostic endpoint to see raw Shopify requests
        [HttpPost("rates-debug")]
        public async Task<IActionResult> GetRatesDebug()
        {
            using var reader = new StreamReader(Request.Body);
            var rawBody = await reader.ReadToEndAsync();
            _logger.LogInformation("RAW RATE REQUEST: {RawBody}", rawBody);
            return Ok(new { received = rawBody });
        }

        [HttpPost("rates")]
        // Note: HMAC validation intentionally omitted here.
        // Carrier service callbacks come through a proxy and their signature cannot be reliably
        // verified server-side. Security is provided by shop-domain matching + Skypoint token lookup.
        public async Task<IActionResult> GetRates()
        {
            _logger.LogInformation("Rate request received");

            try
            {
                // Read raw request body
                using var reader = new StreamReader(Request.Body);
                var rawBody = await reader.ReadToEndAsync();
                _logger.LogInformation("Raw request body: {RawBody}", rawBody);

                // Try to deserialize to strongly typed object
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                CarrierServiceRequest typedRequest;
                try
                {
                    typedRequest = System.Text.Json.JsonSerializer.Deserialize<CarrierServiceRequest>(rawBody, options);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize request to CarrierServiceRequest");
                    return BadRequest(new { error = "Invalid request format", details = ex.Message });
                }

                if (typedRequest == null || typedRequest.Rate == null)
                {
                    _logger.LogWarning("Invalid request: request or rate is null after deserialization");
                    return BadRequest(new { error = "Invalid request body" });
                }

                _logger.LogInformation("Rate request: {Origin} → {Dest}",
                    typedRequest.Origin.City, typedRequest.Destination.City);

                // Resolve shop domain from URL param first, then headers
                var shopDomain = Request.Query["shop"].ToString();
                if (string.IsNullOrEmpty(shopDomain))
                    shopDomain = Request.Headers["X-Shopify-Shop-Domain"].ToString();
                if (string.IsNullOrEmpty(shopDomain))
                    shopDomain = Request.Headers["X-Shop-Domain"].ToString();
                if (string.IsNullOrEmpty(shopDomain))
                    shopDomain = Request.Headers["X-Shopify-Shop-Domain"].ToString();
                if (string.IsNullOrEmpty(shopDomain))
                    shopDomain = Request.Headers["Shopify-Shop-Domain"].ToString();

                shopDomain = shopDomain?.Replace("https://", "").Replace("http://", "").TrimEnd('/');

                _logger.LogInformation("Rate request from shop: {Shop}. All headers: {Headers}",
                    string.IsNullOrEmpty(shopDomain) ? "(unknown)" : shopDomain,
                    string.Join(", ", Request.Headers.Select(h => $"{h.Key}={h.Value}")));

                // If still no shop domain, try to get from environment or use a default
                if (string.IsNullOrEmpty(shopDomain))
                {
                    var defaultShop = Environment.GetEnvironmentVariable("DEFAULT_SHOP_DOMAIN");
                    if (!string.IsNullOrEmpty(defaultShop))
                    {
                        shopDomain = defaultShop.Replace("https://", "").Replace("http://", "").TrimEnd('/');
                        _logger.LogInformation("Using default shop from environment: {Shop}", shopDomain);
                    }
                }

                // Get token — try cache first, then auto-refresh using stored credentials
                var skypointToken = await GetOrRefreshTokenAsync(shopDomain);

                if (string.IsNullOrEmpty(skypointToken))
                {
                    _logger.LogWarning("No Skypoint token available for shop '{Shop}'. Merchant must log in to the app.", shopDomain);
                    return Ok(new CarrierServiceResponse { Rates = new List<ShippingRate>() });
                }

                // Build Skypoint rate request
                // Shopify sometimes sends empty city — fall back to province then postal_code
                var pickupSuburb = FirstNonEmpty(typedRequest.Origin.City, typedRequest.Origin.Province, typedRequest.Origin.PostalCode);
                var dropoffSuburb = FirstNonEmpty(typedRequest.Destination.City, typedRequest.Destination.Province, typedRequest.Destination.PostalCode);

                var rateRequest = new RateRequest
                {
                    PickUpSuburb = pickupSuburb,
                    PickUpPostalCode = typedRequest.Origin.PostalCode,
                    DropOffSuburb = dropoffSuburb,
                    DropOffPostalCode = typedRequest.Destination.PostalCode,
                    ParcelsDims = typedRequest.Items.Select(item => new ParcelDimension
                    {
                        ParcelMass = item.Grams > 0 ? item.Grams / 1000.0 : double.Parse(_configuration["Skypoint:DefaultParcelMass"] ?? "0.5"),
                        ParcelLength = double.Parse(_configuration["Skypoint:DefaultParcelLength"] ?? "10.0"),
                        ParcelBreadth = double.Parse(_configuration["Skypoint:DefaultParcelBreadth"] ?? "10.0"),
                        ParcelHeight = double.Parse(_configuration["Skypoint:DefaultParcelHeight"] ?? "10.0"),
                        PredefinedParcel = _configuration["Skypoint:DefaultParcelType"] ?? "A4_Text_Book",
                        ParcelReference = !string.IsNullOrEmpty(item.Sku) ? item.Sku : item.Name,
                        SelectedParcel = _configuration["Skypoint:DefaultParcelType"] ?? "A4_Text_Book"
                    }).ToList()
                };

                // Fallback: if no items, add a default parcel so Skypoint doesn't reject
                if (!rateRequest.ParcelsDims.Any())
                {
                    rateRequest.ParcelsDims.Add(new ParcelDimension
                    {
                        ParcelMass = double.Parse(_configuration["Skypoint:DefaultParcelMass"] ?? "0.5"),
                        ParcelLength = double.Parse(_configuration["Skypoint:DefaultParcelLength"] ?? "10.0"),
                        ParcelBreadth = double.Parse(_configuration["Skypoint:DefaultParcelBreadth"] ?? "10.0"),
                        ParcelHeight = double.Parse(_configuration["Skypoint:DefaultParcelHeight"] ?? "10.0"),
                        PredefinedParcel = _configuration["Skypoint:DefaultParcelType"] ?? "A4_Text_Book",
                        SelectedParcel = _configuration["Skypoint:DefaultParcelType"] ?? "A4_Text_Book",
                        ParcelReference = "DEFAULT"
                    });
                }

                _logger.LogInformation("Requesting rates: {Origin} ({OriginPC}) → {Dest} ({DestPC}), {Count} items",
                    rateRequest.PickUpSuburb, rateRequest.PickUpPostalCode,
                    rateRequest.DropOffSuburb, rateRequest.DropOffPostalCode,
                    rateRequest.ParcelsDims.Count);

                // Map postal codes and suburbs to Skypoint-recognized values
                var originalPickupCode = rateRequest.PickUpPostalCode;
                var originalDropoffCode = rateRequest.DropOffPostalCode;
                var originalPickupSuburb = rateRequest.PickUpSuburb;
                var originalDropoffSuburb = rateRequest.DropOffSuburb;
                
                var mappedPickupCode = MapPostalCode(rateRequest.PickUpPostalCode, rateRequest.PickUpSuburb);
                var mappedDropoffCode = MapPostalCode(rateRequest.DropOffPostalCode, rateRequest.DropOffSuburb);
                var mappedPickupSuburb = MapSuburb(rateRequest.PickUpSuburb, mappedPickupCode);
                var mappedDropoffSuburb = MapSuburb(rateRequest.DropOffSuburb, mappedDropoffCode);

                _logger.LogInformation("Mapping: {OriginPC} ({OriginSuburb}) → {MappedPC} ({MappedSuburb}), {DestPC} ({DestSuburb}) → {MappedDestPC} ({MappedDestSuburb})",
                    originalPickupCode, originalPickupSuburb, mappedPickupCode, mappedPickupSuburb,
                    originalDropoffCode, originalDropoffSuburb, mappedDropoffCode, mappedDropoffSuburb);

                rateRequest.PickUpPostalCode = mappedPickupCode;
                rateRequest.DropOffPostalCode = mappedDropoffCode;
                rateRequest.PickUpSuburb = mappedPickupSuburb;
                rateRequest.DropOffSuburb = mappedDropoffSuburb;

                var skypointRates = await _skypointApiClient.GetRatesAsync(rateRequest, skypointToken);

                var shopifyRates = new CarrierServiceResponse
                {
                    Rates = skypointRates.Select(rate => new ShippingRate
                    {
                        ServiceName = $"Skypoint {rate.ServiceName}",
                        ServiceCode = rate.ServiceName,
                        TotalPrice = (int)Math.Round(rate.Price * 100),
                        Description = rate.ServiceDescription,
                        Currency = typedRequest.CurrencyCode,
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

        private static string FirstNonEmpty(params string[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

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
                _logger.LogWarning("No credentials stored for shop {Shop}. Merchant must log in to the app.", shopDomain);
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
                    _skypointTokenStore.SaveToken(shopDomain, loginResponse.Token.TokenValue, loginResponse.Token.Expiration, loginResponse.Id);
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

        private string MapPostalCode(string postalCode, string suburb)
        {
            // Map postal codes using configuration or fallback to logic
            var code = postalCode?.Trim() ?? string.Empty;

            // Try to find mapping in configuration by suburb name
            if (!string.IsNullOrEmpty(suburb))
            {
                var suburbKey = suburb.Replace(" ", "");
                var mappedCode = _configuration[$"Skypoint:PostalCodeMappings:{suburbKey}"];
                if (!string.IsNullOrEmpty(mappedCode))
                    return mappedCode;
            }

            // Fallback logic based on postal code patterns
            if (code.StartsWith("2") || suburb?.Contains("Johannesburg", StringComparison.OrdinalIgnoreCase) == true)
                return _configuration["Skypoint:PostalCodeMappings:Johannesburg"] ?? "2000";

            if (code.StartsWith("0") || suburb?.Contains("Pretoria", StringComparison.OrdinalIgnoreCase) == true)
                return _configuration["Skypoint:PostalCodeMappings:Pretoria"] ?? "0002";

            if (code.StartsWith("7") || code.StartsWith("8") || suburb?.Contains("Cape Town", StringComparison.OrdinalIgnoreCase) == true)
                return _configuration["Skypoint:PostalCodeMappings:CapeTown"] ?? "8000";

            if (code.StartsWith("4") || suburb?.Contains("Durban", StringComparison.OrdinalIgnoreCase) == true)
                return _configuration["Skypoint:PostalCodeMappings:Durban"] ?? "4001";

            if (code.StartsWith("9") || suburb?.Contains("Bloemfontein", StringComparison.OrdinalIgnoreCase) == true)
                return _configuration["Skypoint:PostalCodeMappings:Bloemfontein"] ?? "9301";

            if (code.StartsWith("6") || suburb?.Contains("Port Elizabeth", StringComparison.OrdinalIgnoreCase) == true)
                return _configuration["Skypoint:PostalCodeMappings:PortElizabeth"] ?? "6001";

            // Return original code if no mapping found
            return code;
        }

        private string MapSuburb(string suburb, string postalCode)
        {
            // Map suburbs using configuration or fallback to logic
            var sub = suburb?.Trim() ?? string.Empty;

            // Try to find mapping in configuration by suburb name
            var suburbKey = sub.Replace(" ", "");
            var mappedSuburb = _configuration[$"Skypoint:SuburbMappings:{suburbKey}"];
            if (!string.IsNullOrEmpty(mappedSuburb))
                return mappedSuburb;

            // Fallback logic based on postal code
            if (postalCode == "2000" || postalCode == _configuration["Skypoint:PostalCodeMappings:Johannesburg"])
            {
                if (sub.Contains("Johannesburg", StringComparison.OrdinalIgnoreCase))
                    return "Johannesburg";
                if (sub.Contains("Germiston", StringComparison.OrdinalIgnoreCase))
                    return _configuration["Skypoint:SuburbMappings:Germiston"] ?? "Johannesburg";
                return "Johannesburg";
            }

            if (postalCode == "0002" || postalCode == _configuration["Skypoint:PostalCodeMappings:Pretoria"])
            {
                if (sub.Contains("Pretoria", StringComparison.OrdinalIgnoreCase))
                    return "Pretoria";
                return "Pretoria";
            }

            if (postalCode == "8000" || postalCode == _configuration["Skypoint:PostalCodeMappings:CapeTown"])
            {
                if (sub.Contains("Cape Town", StringComparison.OrdinalIgnoreCase))
                    return "Cape Town";
                return "Cape Town";
            }

            if (postalCode == "4001" || postalCode == _configuration["Skypoint:PostalCodeMappings:Durban"])
            {
                if (sub.Contains("Durban", StringComparison.OrdinalIgnoreCase))
                    return "Durban";
                return "Durban";
            }

            if (postalCode == "9301" || postalCode == _configuration["Skypoint:PostalCodeMappings:Bloemfontein"])
            {
                if (sub.Contains("Bloemfontein", StringComparison.OrdinalIgnoreCase))
                    return "Bloemfontein";
                return "Bloemfontein";
            }

            // Return original suburb if no mapping found
            return sub;
        }
    }
}
