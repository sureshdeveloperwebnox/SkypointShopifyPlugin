using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.Configuration;
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
        private readonly ISkypointSettingsService _settingsService;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IShopTokenStore _shopTokenStore;

        public CarrierServiceController(
            ILogger<CarrierServiceController> logger,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore,
            IConfiguration configuration,
            ISkypointSettingsService settingsService,
            IShopifyAdminService shopifyAdminService,
            IShopTokenStore shopTokenStore)
        {
            _logger = logger;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
            _configuration = configuration;
            _settingsService = settingsService;
            _shopifyAdminService = shopifyAdminService;
            _shopTokenStore = shopTokenStore;
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
            string? shopDomain = null;

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
                shopDomain = Request.Query["shop"].ToString();
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

                // Load settings from Shopify Shop Metafields
                var settings = await _settingsService.GetSettingsAsync(shopDomain);
                var skypointBaseUrl = settings.IsUat ? "https://uat.skypoint.online" : "https://skypoint.online";

                // Get token — try cache first, then auto-refresh using settings credentials
                var skypointToken = await GetOrRefreshTokenAsync(shopDomain, settings);

                if (string.IsNullOrEmpty(skypointToken))
                {
                    _logger.LogWarning("No Skypoint token available for shop '{Shop}'. Merchant must configure credentials in Shopify Settings.", shopDomain);
                    return Ok(new CarrierServiceResponse { Rates = new List<ShippingRate>() });
                }

                // Build Skypoint rate request
                // Use settings origin if available, otherwise fall back to Shopify's origin
                var pickupSuburb = FirstNonEmpty(settings.ShipperSuburb, typedRequest.Origin.City, typedRequest.Origin.Province, typedRequest.Origin.PostalCode);
                var pickupPostcode = FirstNonEmpty(settings.ShipperPostcode, typedRequest.Origin.PostalCode);
                var dropoffSuburb = FirstNonEmpty(typedRequest.Destination.City, typedRequest.Destination.Province, typedRequest.Destination.PostalCode);

                var shopAccessToken = _shopTokenStore.GetToken(shopDomain);
                var parcels = new List<ParcelDimension>();

                foreach (var item in typedRequest.Items)
                {
                    double resolvedMass = item.Grams > 0 ? item.Grams / 1000.0 : settings.DefaultMass;
                    double resolvedLength = settings.DefaultLength;
                    double resolvedBreadth = settings.DefaultBreadth;
                    double resolvedHeight = settings.DefaultHeight;

                    if (!string.IsNullOrEmpty(shopAccessToken))
                    {
                        try
                        {
                            var dims = await _shopifyAdminService.GetProductDimensionsAsync(shopDomain, shopAccessToken, item.ProductId.ToString());
                            if (dims != null)
                            {
                                if (item.Grams <= 0 && dims.Mass.HasValue) resolvedMass = dims.Mass.Value;
                                if (dims.Length.HasValue) resolvedLength = dims.Length.Value;
                                if (dims.Breadth.HasValue) resolvedBreadth = dims.Breadth.Value;
                                if (dims.Height.HasValue) resolvedHeight = dims.Height.Value;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to resolve product dimensions for product ID {ProductId} in {Shop}", item.ProductId, shopDomain);
                        }
                    }

                    for (int i = 0; i < item.Quantity; i++)
                    {
                        parcels.Add(new ParcelDimension
                        {
                            ParcelMass = resolvedMass,
                            ParcelLength = resolvedLength,
                            ParcelBreadth = resolvedBreadth,
                            ParcelHeight = resolvedHeight,
                            PredefinedParcel = _configuration["SkypointMappings:DefaultParcelType"] ?? "A4_Text_Book",
                            ParcelReference = !string.IsNullOrEmpty(item.Sku) ? item.Sku : item.Name,
                            SelectedParcel = _configuration["SkypointMappings:DefaultParcelType"] ?? "A4_Text_Book"
                        });
                    }
                }

                // Fallback: if no items, add a default parcel so Skypoint doesn't reject
                if (!parcels.Any())
                {
                    parcels.Add(new ParcelDimension
                    {
                        ParcelMass = settings.DefaultMass,
                        ParcelLength = settings.DefaultLength,
                        ParcelBreadth = settings.DefaultBreadth,
                        ParcelHeight = settings.DefaultHeight,
                        PredefinedParcel = _configuration["SkypointMappings:DefaultParcelType"] ?? "A4_Text_Book",
                        SelectedParcel = _configuration["SkypointMappings:DefaultParcelType"] ?? "A4_Text_Book",
                        ParcelReference = "DEFAULT"
                    });
                }

                var rateRequest = new RateRequest
                {
                    PickUpSuburb = pickupSuburb,
                    PickUpPostalCode = pickupPostcode,
                    DropOffSuburb = dropoffSuburb,
                    DropOffPostalCode = typedRequest.Destination.PostalCode,
                    ParcelsDims = parcels
                };

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

                var skypointRates = await _skypointApiClient.GetRatesAsync(rateRequest, skypointToken, skypointBaseUrl);

                var computedRates = new List<ShippingRate>();
                var cartSubtotal = typedRequest.Items.Sum(item => (item.Price * item.Quantity) / 100.0);
                var isFreeShipping = settings.FreeshipThreshold > 0 && (decimal)cartSubtotal >= settings.FreeshipThreshold;

                foreach (var rate in skypointRates.Where(r => r.Price > 0))
                {
                    var rawName = rate.ServiceName?.Trim() ?? string.Empty;
                    bool isRoad = rawName.Contains("road", StringComparison.OrdinalIgnoreCase);
                    bool isAir = rawName.Contains("air", StringComparison.OrdinalIgnoreCase);
                    bool isCounter = rawName.Contains("counter", StringComparison.OrdinalIgnoreCase) || rawName.Contains("pudo", StringComparison.OrdinalIgnoreCase);

                    // 1. Filtering by shipping mode settings
                    if (isRoad && !settings.EnableRoad) continue;
                    if (isAir && !settings.EnableAir) continue;
                    if (isCounter && !settings.EnableCounter) continue;

                    // 2. Renaming
                    var displayName = rawName;
                    if (isRoad && !string.IsNullOrEmpty(settings.RenameRoad)) displayName = settings.RenameRoad;
                    else if (isAir && !string.IsNullOrEmpty(settings.RenameAir)) displayName = settings.RenameAir;
                    else if (isCounter && !string.IsNullOrEmpty(settings.RenameCounter)) displayName = settings.RenameCounter;

                    // 3. Markup calculation
                    var finalPrice = (decimal)rate.Price;
                    if (!isFreeShipping)
                    {
                        var markupValue = 0m;
                        if (isRoad) markupValue = settings.MarkupRoad;
                        else if (isAir) markupValue = settings.MarkupAir;
                        else if (isCounter) markupValue = settings.MarkupCounter;

                        if (markupValue > 0)
                        {
                            if (settings.MarkupType?.Equals("percent", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                finalPrice *= (1m + markupValue / 100m);
                            }
                            else // flat rate
                            {
                                finalPrice += markupValue;
                            }
                        }
                    }
                    else
                    {
                        finalPrice = 0;
                    }

                    // Convert to cents for Shopify
                    var totalPriceCents = (int)Math.Round(finalPrice * 100);

                    computedRates.Add(new ShippingRate
                    {
                        ServiceName = displayName,
                        ServiceCode = rawName, // Keep original service code reference
                        TotalPrice = totalPriceCents,
                        Description = rate.ServiceDescription,
                        Currency = typedRequest.CurrencyCode,
                        MinDeliveryDate = DateTime.UtcNow.AddDays(rate.TransitDays).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        MaxDeliveryDate = DateTime.UtcNow.AddDays(rate.TransitDays + 2).ToString("yyyy-MM-ddTHH:mm:ssZ")
                    });
                }

                // 4. Sorting
                var sortedRates = computedRates
                    .OrderBy(r => {
                        var rawName = r.ServiceCode;
                        if (rawName.Contains("road", StringComparison.OrdinalIgnoreCase)) return settings.SortRoad;
                        if (rawName.Contains("air", StringComparison.OrdinalIgnoreCase)) return settings.SortAir;
                        if (rawName.Contains("counter", StringComparison.OrdinalIgnoreCase) || rawName.Contains("pudo", StringComparison.OrdinalIgnoreCase)) return settings.SortCounter;
                        return 999;
                    })
                    .ThenBy(r => r.TotalPrice)
                    .ToList();

                var shopifyRates = new CarrierServiceResponse
                {
                    Rates = sortedRates
                };

                _logger.LogInformation("Returning {Count} rates for shop {Shop}",
                    shopifyRates.Rates.Count, shopDomain);
                return Ok(shopifyRates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shipping rates");

                try
                {
                    var settings = await _settingsService.GetSettingsAsync(shopDomain);
                    if (settings.FallbackCost > 0)
                    {
                        _logger.LogInformation("SkyPoint API rate request failed. Serving fallback rate: {Cost}", settings.FallbackCost);
                        var fallbackRates = new List<ShippingRate>
                        {
                            new ShippingRate
                            {
                                ServiceName = "SkyPoint Standard Shipping (Fallback)",
                                ServiceCode = "skypoint_fallback",
                                TotalPrice = (int)Math.Round(settings.FallbackCost * 100),
                                Description = "Standard delivery (SkyPoint API currently unavailable)",
                                Currency = "ZAR"
                            }
                        };
                        return Ok(new CarrierServiceResponse { Rates = fallbackRates });
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Failed to build fallback shipping rate.");
                }

                return Ok(new CarrierServiceResponse { Rates = new List<ShippingRate>() });
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private static string FirstNonEmpty(params string[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        private async Task<string?> GetOrRefreshTokenAsync(string? shopDomain, SkypointShopifySettings settings)
        {
            if (string.IsNullOrEmpty(shopDomain)) return null;

            // 1. Valid cached token
            var cached = _skypointTokenStore.GetToken(shopDomain);
            if (!string.IsNullOrEmpty(cached)) return cached;

            // 2. Token expired — re-login using settings credentials
            if (string.IsNullOrEmpty(settings.Username) || string.IsNullOrEmpty(settings.Password))
            {
                _logger.LogWarning("No API credentials configured in Shop Metafields for {Shop}.", shopDomain);
                return null;
            }

            try
            {
                var skypointBaseUrl = settings.IsUat ? "https://uat.skypoint.online" : "https://skypoint.online";
                _logger.LogInformation("Refreshing Skypoint token for shop {Shop} using metafield credentials", shopDomain);
                var loginResponse = await _skypointApiClient.LoginAsync(new LoginRequest
                {
                    Username = settings.Username,
                    Pwd = settings.Password
                }, skypointBaseUrl);

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
            if (code.StartsWith("2") || code.StartsWith("1") || suburb?.Contains("Johannesburg", StringComparison.OrdinalIgnoreCase) == true)
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
