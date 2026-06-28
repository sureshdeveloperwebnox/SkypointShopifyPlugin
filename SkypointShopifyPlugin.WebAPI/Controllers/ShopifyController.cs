using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Application.Common;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/shopify")]
    public class ShopifyController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly ILogger<ShopifyController> _logger;
        private readonly IShopifyOAuthService _oauthService;
        private readonly IConfiguration _configuration;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IShopTokenStore _shopTokenStore;

        public ShopifyController(IMediator mediator, ILogger<ShopifyController> logger, IShopifyOAuthService oauthService, IConfiguration configuration, ISkypointApiClient skypointApiClient, IShopifyAdminService shopifyAdminService, IShopTokenStore shopTokenStore)
        {
            _mediator = mediator;
            _logger = logger;
            _oauthService = oauthService;
            _configuration = configuration;
            _skypointApiClient = skypointApiClient;
            _shopifyAdminService = shopifyAdminService;
            _shopTokenStore = shopTokenStore;
        }

        /// <summary>
        /// Default endpoint - Shopify store requesting to install app, or app opened from admin
        /// </summary>
        [HttpGet]
        public IActionResult Default(string shop)
        {
            if (string.IsNullOrEmpty(shop))
                return Redirect("/index.html");

            _logger.LogInformation("Install request from shop: {Shop}", shop);
            try
            {
                // Build redirect URI dynamically - works with any ngrok URL, no config needed
                var redirectUri = $"{Request.Scheme}://{Request.Host}/api/shopify/auth";
                var installUrl = _oauthService.GetInstallUrl(shop, redirectUri);
                return Redirect(installUrl);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid shop domain: {Shop}", shop);
                return BadRequest(new { error = "Invalid shop domain" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating install URL for shop: {Shop}", shop);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// OAuth callback - Shopify redirects here after user approves.
        /// Automatically saves token + registers carrier service for the store.
        /// </summary>
        [HttpGet("auth")]
        public async Task<IActionResult> Auth(string shop, string code, string state)
        {
            _logger.LogInformation("Auth callback from shop: {Shop}", shop);

            try
            {
                if (string.IsNullOrEmpty(code))
                {
                    _logger.LogError("No authorization code provided");
                    return BadRequest(new { error = "Authorization code is required" });
                }

                // Exchange code for access token
                var accessToken = await _oauthService.ExchangeCodeForAccessTokenAsync(shop, code);
                if (string.IsNullOrEmpty(accessToken))
                    return StatusCode(500, new { error = "Failed to obtain access token" });

                _logger.LogInformation("Access token obtained for shop: {Shop}", shop);

                // Persist token immediately - survives restarts
                _shopTokenStore.SaveToken(shop, accessToken);

                // Register carrier service in background - don't block the redirect
                // even if it fails now, it will retry on next dashboard open
                var carrierUrl = $"{Request.Scheme}://{Request.Host}/api/carrier/rates?shop={Uri.EscapeDataString(shop)}";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _shopifyAdminService.RegisterAndAssignCarrierServiceAsync(shop, accessToken, carrierUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background carrier registration failed for {Shop} - will retry on next login", shop);
                    }
                });

                // Redirect to app UI immediately
                return Redirect($"/index.html?shop={Uri.EscapeDataString(shop)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in auth callback for shop: {Shop}", shop);
                return StatusCode(500, new { error = "Installation failed: " + ex.Message });
            }
        }

        /// <summary>
        /// Setup endpoint — called after dashboard open to ensure carrier service is registered
        /// and the callback URL is current (e.g. after ngrok restart).
        /// GET /api/shopify/setup?shop=yourstore.myshopify.com
        /// </summary>
        [HttpGet("setup")]
        public async Task<IActionResult> Setup([FromQuery] string shop)
        {
            if (string.IsNullOrEmpty(shop))
                return BadRequest(new { error = "shop parameter required" });

            shop = shop.Replace("https://", "").Replace("http://", "").TrimEnd('/').ToLowerInvariant();

            // 1. Primary: use the OAuth token saved when this store installed the app
            var accessToken = _shopTokenStore.GetToken(shop);

            // 2. Fallback: try legacy app client_credentials (for stores installed with old app ID)
            //    This only works for apps that support the offline-token client_credentials grant.
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogInformation("No stored token for {Shop} — trying legacy client_credentials fallback", shop);
                accessToken = await _oauthService.GetTokenViaClientCredentialsAsync(shop);
                if (!string.IsNullOrEmpty(accessToken))
                    _shopTokenStore.SaveToken(shop, accessToken); // persist so we don't need to do this again
            }

            // 3. No token at all — store needs to go through the OAuth flow
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("No token found for {Shop} — reinstall required", shop);
                return Ok(new
                {
                    status = "reinstall_required",
                    success = false,
                    message = "This store needs to reinstall the Skypoint Shipping app to authorize it. " +
                              "Ask the merchant to visit the install URL and complete the OAuth flow."
                });
            }

            var result = await RegisterCarrierForShop(shop, accessToken);
            return Ok(result);
        }


        // ── shared helper ─────────────────────────────────────────────────────────
        private async Task<object> RegisterCarrierForShop(string shop, string accessToken)
        {
            // Build the public callback URL from the incoming request.
            // When accessed via ngrok, X-Forwarded-Proto = "https" is applied by UseForwardedHeaders
            // so Request.Scheme is already "https" — the URL will be correct.
            // Shopify REQUIRES HTTPS for carrier callback URLs, so refuse http:// here.
            var carrierServiceUrl = $"{Request.Scheme}://{Request.Host}/api/carrier/rates?shop={Uri.EscapeDataString(shop)}";

            if (!carrierServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Carrier registration skipped for {Shop} — callback URL is not HTTPS: {Url}. " +
                    "Open /api/shopify/setup through the ngrok URL (e.g. https://xxxx.ngrok-free.app/api/shopify/setup?shop={Shop})",
                    shop, carrierServiceUrl, shop);
                return new
                {
                    success = false,
                    message = "Carrier registration requires an HTTPS callback URL. " +
                              "Please call this endpoint through the ngrok tunnel, not localhost.",
                    shop,
                    carrier_url = carrierServiceUrl
                };
            }

            _logger.LogInformation("Registering carrier for {Shop} → {Url}", shop, carrierServiceUrl);

            var (success, message) = await _shopifyAdminService.RegisterAndAssignCarrierServiceAsync(
                shop, accessToken, carrierServiceUrl);

            if (!success && IsInvalidShopifyTokenMessage(message))
            {
                _shopTokenStore.RemoveToken(shop);
                var redirectUri = $"{Request.Scheme}://{Request.Host}/api/shopify/auth";
                var installUrl = _oauthService.GetInstallUrl(shop, redirectUri);
                _logger.LogWarning("Removed invalid Shopify token for {Shop}; OAuth reconnect required", shop);

                return new
                {
                    status = "reinstall_required",
                    success = false,
                    message = "Shopify rejected the saved access token. Reconnect the app to authorize Skypoint Shipping again.",
                    shop,
                    install_url = installUrl,
                    carrier_url = carrierServiceUrl
                };
            }

            if (success)
                _logger.LogInformation("Carrier setup OK for {Shop}: {Msg}", shop, message);
            else
                _logger.LogWarning("Carrier setup failed for {Shop}: {Msg}", shop, message);

            return new { success, message, shop, carrier_url = carrierServiceUrl };
        }

        private static bool IsInvalidShopifyTokenMessage(string message)
            => message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Invalid API key or access token", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unrecognized login", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Webhook endpoint for orders/create
        /// </summary>
        [HttpPost("orders/create")]
        public async Task<IActionResult> OrdersCreate()
        {
            _logger.LogInformation("Order create webhook received");
            
            if (!VerifyWebhookSignature())
            {
                _logger.LogWarning("Invalid webhook signature");
                return Unauthorized();
            }

            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                var shopifyOrder = JsonSerializer.Deserialize<ShopifyOrderWebhook>(body);

                if (shopifyOrder == null)
                {
                    _logger.LogError("Failed to deserialize Shopify order");
                    return BadRequest();
                }

                _logger.LogInformation("Processing order {OrderId} for shop", shopifyOrder.id);

                // Login to Skypoint API
                var username = _configuration["Skypoint:Username"];
                var password = _configuration["Skypoint:Password"];

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    _logger.LogError("Skypoint credentials not configured");
                    return StatusCode(500, new { error = "Skypoint credentials not configured" });
                }

                var loginResponse = await _skypointApiClient.LoginAsync(new LoginRequest
                {
                    Username = username,
                    Pwd = password
                });

                if (loginResponse == null || string.IsNullOrEmpty(loginResponse.Token?.TokenValue))
                {
                    _logger.LogError("Failed to login to Skypoint API");
                    return StatusCode(500, new { error = "Failed to login to Skypoint API" });
                }

                // Convert Shopify order to Skypoint booking request
                var bookingRequest = MapShopifyOrderToSkypointBooking(shopifyOrder);

                // Create booking in Skypoint
                var bookingResponse = await _skypointApiClient.CreateBookingAsync(bookingRequest, loginResponse.Token.TokenValue);

                if (bookingResponse != null)
                {
                    _logger.LogInformation("Successfully created Skypoint booking for Shopify order {OrderId}", shopifyOrder.id);
                    return Ok(new { 
                        message = "Order processed successfully", 
                        shopifyOrderId = shopifyOrder.id,
                        skypointBookingId = bookingResponse.Id
                    });
                }
                else
                {
                    _logger.LogError("Failed to create Skypoint booking for Shopify order {OrderId}", shopifyOrder.id);
                    return StatusCode(500, new { error = "Failed to create Skypoint booking" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order create webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Webhook endpoint for orders/updated
        /// </summary>
        [HttpPost("orders/updated")]
        public async Task<IActionResult> OrdersUpdated()
        {
            _logger.LogInformation("Order updated webhook received");
            
            if (!VerifyWebhookSignature())
            {
                _logger.LogWarning("Invalid webhook signature");
                return Unauthorized();
            }

            // TODO: Process order update
            var body = await new StreamReader(Request.Body).ReadToEndAsync();
            _logger.LogInformation("Order updated payload: {Body}", body);
            
            return Ok();
        }

        /// <summary>
        /// Webhook endpoint for orders/cancelled
        /// </summary>
        [HttpPost("orders/cancelled")]
        public async Task<IActionResult> OrdersCancelled()
        {
            _logger.LogInformation("Order cancelled webhook received");
            
            if (!VerifyWebhookSignature())
            {
                _logger.LogWarning("Invalid webhook signature");
                return Unauthorized();
            }

            // TODO: Process order cancellation
            var body = await new StreamReader(Request.Body).ReadToEndAsync();
            _logger.LogInformation("Order cancelled payload: {Body}", body);
            
            return Ok();
        }

        /// <summary>
        /// Webhook endpoint for app/uninstalled
        /// </summary>
        [HttpPost("app/uninstalled")]
        public async Task<IActionResult> AppUninstalled()
        {
            _logger.LogInformation("App uninstalled webhook received");
            
            if (!VerifyWebhookSignature())
            {
                _logger.LogWarning("Invalid webhook signature");
                return Unauthorized();
            }

            // TODO: Clean up shop data
            var body = await new StreamReader(Request.Body).ReadToEndAsync();
            _logger.LogInformation("App uninstalled payload: {Body}", body);
            
            return Ok();
        }

        private bool VerifyWebhookSignature()
        {
            Request.Headers.TryGetValue("X-Shopify-Hmac-Sha256", out var signature);
            
            if (string.IsNullOrEmpty(signature))
            {
                return false;
            }

            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var body = reader.ReadToEnd();
            Request.Body.Position = 0;

            var webhookSecret = _configuration["Shopify:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret) || webhookSecret == "YOUR_WEBHOOK_SECRET")
            {
                _logger.LogWarning("Webhook secret not configured");
                return false;
            }

            return _oauthService.VerifyWebhookSignature(body, signature.ToString(), webhookSecret);
        }

        private BookingRequest MapShopifyOrderToSkypointBooking(ShopifyOrderWebhook shopifyOrder)
        {
            var shippingAddress = shopifyOrder.shipping_address;
            var billingAddress = shopifyOrder.billing_address;
            var customer = shopifyOrder.customer;

            return new BookingRequest
            {
                UserId = customer?.id ?? shopifyOrder.id,
                PickUpAddress = billingAddress?.address1 ?? string.Empty,
                DropOffAddress = shippingAddress?.address1 ?? string.Empty,
                FromSuburb = billingAddress?.city ?? string.Empty,
                ToSuburb = shippingAddress?.city ?? string.Empty,
                PickUpPCode = billingAddress?.zip ?? string.Empty,
                DropOffPCode = shippingAddress?.zip ?? string.Empty,
                Comment = $"Shopify Order #{shopifyOrder.order_number}",
                Province = billingAddress?.province ?? string.Empty,
                DestinationProvince = shippingAddress?.province ?? string.Empty,
                DropOff = new DropOffPerson
                {
                    FirstName = shippingAddress?.first_name ?? customer?.first_name ?? string.Empty,
                    LastName = shippingAddress?.last_name ?? customer?.last_name ?? string.Empty,
                    Phone = shippingAddress?.phone ?? customer?.phone ?? string.Empty,
                    Email = customer?.email ?? string.Empty,
                    Suburb = shippingAddress?.city ?? string.Empty,
                    City = shippingAddress?.city ?? string.Empty,
                    State = shippingAddress?.province ?? string.Empty,
                    Zip = shippingAddress?.zip ?? string.Empty
                },
                PickUp = new PickUpPerson
                {
                    FirstName = billingAddress?.first_name ?? customer?.first_name ?? string.Empty,
                    LastName = billingAddress?.last_name ?? customer?.last_name ?? string.Empty,
                    Phone = customer?.phone ?? string.Empty,
                    Email = customer?.email ?? string.Empty,
                    Suburb = billingAddress?.city ?? string.Empty,
                    City = billingAddress?.city ?? string.Empty,
                    State = billingAddress?.province ?? string.Empty,
                    Zip = billingAddress?.zip ?? string.Empty
                },
                Type = "ROAD",
                PickUpDate = shopifyOrder.created_at.ToString("yyyy-MM-dd"),
                PickUpTime = shopifyOrder.created_at.ToString("HH:mm"),
                ParcelDimensions = shopifyOrder.line_items.Select(item => new ParcelDimension
                {
                    ParcelMass = 1.0,
                    ParcelLength = 10.0,
                    ParcelBreadth = 10.0,
                    ParcelHeight = 10.0,
                    ParcelReference = item.sku ?? string.Empty
                }).ToList(),
                PickUpCity = billingAddress?.city ?? string.Empty,
                DropOffCity = shippingAddress?.city ?? string.Empty,
                PickUpZip = billingAddress?.zip ?? string.Empty,
                DropOffZip = shippingAddress?.zip ?? string.Empty,
                ShipmentType = "PARCEL",
                PickUpCountry = billingAddress?.country ?? string.Empty
            };
        }
    }
}
