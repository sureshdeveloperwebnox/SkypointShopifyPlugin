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
        private static readonly Dictionary<string, BookingResponse> ProcessedOrderBookings = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object ProcessedOrderBookingsLock = new();

        private readonly IMediator _mediator;
        private readonly ILogger<ShopifyController> _logger;
        private readonly IShopifyOAuthService _oauthService;
        private readonly IConfiguration _configuration;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IShopTokenStore _shopTokenStore;
        private readonly ISkypointTokenStore _skypointTokenStore;

        public ShopifyController(IMediator mediator, ILogger<ShopifyController> logger, IShopifyOAuthService oauthService, IConfiguration configuration, ISkypointApiClient skypointApiClient, IShopifyAdminService shopifyAdminService, IShopTokenStore shopTokenStore, ISkypointTokenStore skypointTokenStore)
        {
            _mediator = mediator;
            _logger = logger;
            _oauthService = oauthService;
            _configuration = configuration;
            _skypointApiClient = skypointApiClient;
            _shopifyAdminService = shopifyAdminService;
            _shopTokenStore = shopTokenStore;
            _skypointTokenStore = skypointTokenStore;
        }

        /// <summary>
        /// Builds the public-facing redirect URI for OAuth.
        /// Uses Shopify__RedirectUri from config — must match the URL registered in Partner Dashboard.
        /// </summary>
        private string BuildRedirectUri()
        {
            var configured = _configuration["Shopify:RedirectUri"];
            if (!string.IsNullOrEmpty(configured))
                return configured;
            return $"{Request.Scheme}://{Request.Host}/api/shopify/auth";
        }

        /// <summary>
        /// Derives the public base URL (scheme + host, no path).
        /// Strips /api/shopify/auth from RedirectUri to get the base.
        /// Used to build the carrier callback URL that Shopify calls at checkout.
        /// </summary>
        private string BuildPublicBaseUrl()
        {
            var configured = _configuration["Shopify:RedirectUri"];
            if (!string.IsNullOrEmpty(configured))
            {
                // e.g. https://xxxx.ngrok-free.app/api/shopify/auth → https://xxxx.ngrok-free.app
                var uri = new Uri(configured);
                return $"{uri.Scheme}://{uri.Host}";
            }
            return $"{Request.Scheme}://{Request.Host}";
        }
        /// <summary>
        /// Default endpoint - Shopify store requesting to install app, or app opened from admin
        /// </summary>
        [HttpGet]
        public IActionResult Default(string shop)
        {
            if (string.IsNullOrEmpty(shop))
                return Redirect("/index.html");

            shop = NormalizeShopDomain(shop);
            _logger.LogInformation("Install request from shop: {Shop}", shop);
            try
            {
                var redirectUri = BuildRedirectUri();
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
                if (string.IsNullOrEmpty(shop))
                    return BadRequest(new { error = "Shop domain is required" });

                shop = NormalizeShopDomain(shop);

                if (string.IsNullOrEmpty(code))
                {
                    _logger.LogError("No authorization code provided");
                    return BadRequest(new { error = "Authorization code is required" });
                }

                // Exchange code for access token
                var accessToken = await _oauthService.ExchangeCodeForAccessTokenAsync(shop, code);
                if (string.IsNullOrEmpty(accessToken))
                    return StatusCode(500, new { error = "Failed to obtain access token" });

                // Persist token immediately — in-memory cache
                _shopTokenStore.SaveToken(shop, accessToken);

                // Register carrier service and webhooks in background — don't block the redirect.
                // Build carrier URL from the public ngrok/production base URL.
                var publicBase = BuildPublicBaseUrl();
                var carrierUrl = $"{publicBase}/api/carrier/rates?shop={Uri.EscapeDataString(shop)}";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Register carrier service
                        await _shopifyAdminService.RegisterAndAssignCarrierServiceAsync(shop, accessToken, carrierUrl);
                        
                        // Register webhooks automatically
                        await _shopifyAdminService.SyncWebhooksAsync(shop, accessToken, publicBase);
                        _logger.LogInformation("Webhooks registered automatically for {Shop}", shop);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background registration failed for {Shop} — will retry on next dashboard open", shop);
                    }
                });

                // Redirect to app UI
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

            shop = NormalizeShopDomain(shop);

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
                var installUrl = _oauthService.GetInstallUrl(shop, BuildRedirectUri());
                return Ok(new
                {
                    status = "reinstall_required",
                    success = false,
                    message = "This store needs to reinstall the Skypoint Shipping app to authorize it. " +
                              "Click 'Reconnect Shopify' to complete the OAuth flow.",
                    shop,
                    install_url = installUrl
                });
            }

            var result = await RegisterCarrierForShop(shop, accessToken);
            return Ok(result);
        }


        private async Task<object> RegisterCarrierForShop(string shop, string accessToken)
        {
            // Always use the public ngrok/production URL for the carrier callback.
            // Shopify REQUIRES HTTPS. Using BuildPublicBaseUrl() ensures we never
            // accidentally register http://localhost as the callback.
            var publicBase = BuildPublicBaseUrl();
            var carrierServiceUrl = $"{publicBase}/api/carrier/rates?shop={Uri.EscapeDataString(shop)}";

            // Register webhooks automatically
            try
            {
                await _shopifyAdminService.SyncWebhooksAsync(shop, accessToken, publicBase);
                _logger.LogInformation("Webhooks synced for {Shop}", shop);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook sync failed for {Shop}", shop);
            }

            if (!carrierServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Carrier registration skipped for {Shop} — callback URL is not HTTPS: {Url}. " +
                    "Update Shopify__RedirectUri in .env with the current ngrok URL.",
                    shop, carrierServiceUrl);
                return new
                {
                    success = false,
                    message = "Carrier registration requires HTTPS. Update Shopify__RedirectUri in .env with your current ngrok URL, then try again.",
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
                var installUrl = _oauthService.GetInstallUrl(shop, BuildRedirectUri());
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

        private static string NormalizeShopDomain(string shop)
            => shop.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                   .Trim()
                   .TrimEnd('/')
                   .ToLowerInvariant();

        /// <summary>
        /// Webhook endpoint for orders/create
        /// </summary>
        [HttpPost("orders/create")]
        public async Task<IActionResult> OrdersCreate()
        {
            _logger.LogInformation("Order create webhook received");
            
            if (!await VerifyWebhookSignature())
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

                lock (ProcessedOrderBookingsLock)
                {
                    if (ProcessedOrderBookings.TryGetValue(shopifyOrder.id, out var existingBooking))
                    {
                        _logger.LogInformation(
                            "Shopify order {OrderId} already processed as Skypoint booking {BookingId}",
                            shopifyOrder.id,
                            existingBooking.Id);

                        return Ok(new
                        {
                            message = "Order already processed",
                            shopifyOrderId = shopifyOrder.id,
                            skypointBookingId = existingBooking.Id,
                            skypointTrackNo = existingBooking.TrackNo,
                            skypointStatus = existingBooking.Status
                        });
                    }
                }

                var shop = Request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault();
                if (string.IsNullOrEmpty(shop))
                {
                    _logger.LogError("Webhook headers missing shop domain");
                    return BadRequest(new { error = "Missing shop domain header" });
                }

                shop = NormalizeShopDomain(shop);
                _logger.LogInformation("Processing order {OrderId} for shop {Shop}", shopifyOrder.id, shop);

                // Retrieve dynamically stored Skypoint token/user id or login using cached credentials.
                var token = _skypointTokenStore.GetToken(shop);
                var skypointUserId = _skypointTokenStore.GetUserId(shop);
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(skypointUserId))
                {
                    var creds = _skypointTokenStore.GetCredentials(shop);
                    if (creds == null)
                    {
                        _logger.LogError("No Skypoint credentials found for shop {Shop}", shop);
                        return BadRequest(new { error = $"Skypoint credentials not configured for shop: {shop}" });
                    }

                    var loginResponse = await _skypointApiClient.LoginAsync(new LoginRequest
                    {
                        Username = creds.Value.username,
                        Pwd = creds.Value.password
                    });

                    if (loginResponse?.Token?.TokenValue == null)
                    {
                        _logger.LogError("Failed to login to Skypoint API for shop {Shop}", shop);
                        return StatusCode(500, new { error = "Failed to login to Skypoint API" });
                    }

                    token = loginResponse.Token.TokenValue;
                    skypointUserId = loginResponse.Id;
                    _skypointTokenStore.SaveToken(shop, token, loginResponse.Token.Expiration, skypointUserId);
                }

                // Convert Shopify order to Skypoint booking request
                var bookingRequest = MapShopifyOrderToSkypointBooking(shopifyOrder, skypointUserId);

                // Create booking in Skypoint
                var bookingResponse = await _skypointApiClient.CreateBookingAsync(bookingRequest, token);

                if (bookingResponse != null)
                {
                    lock (ProcessedOrderBookingsLock)
                    {
                        ProcessedOrderBookings[shopifyOrder.id] = bookingResponse;
                    }

                    _logger.LogInformation("Successfully created Skypoint booking for Shopify order {OrderId}", shopifyOrder.id);
                    return Ok(new { 
                        message = "Order processed successfully", 
                        shopifyOrderId = shopifyOrder.id,
                        skypointBookingId = bookingResponse.Id,
                        skypointTrackNo = bookingResponse.TrackNo,
                        skypointStatus = bookingResponse.Status
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
            
            if (!await VerifyWebhookSignature())
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
            
            if (!await VerifyWebhookSignature())
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
        /// Webhook endpoint for app/uninstalled — removes the stored OAuth token for the shop.
        /// </summary>
        [HttpPost("app/uninstalled")]
        public async Task<IActionResult> AppUninstalled()
        {
            _logger.LogInformation("App uninstalled webhook received");

            if (!await VerifyWebhookSignature())
            {
                _logger.LogWarning("Invalid webhook signature");
                return Unauthorized();
            }

            // Remove the stored token so the next install triggers a clean OAuth flow
            var shopHeader = Request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault();
            if (!string.IsNullOrEmpty(shopHeader))
            {
                _shopTokenStore.RemoveToken(shopHeader);
                _logger.LogInformation("Removed stored token for uninstalled shop: {Shop}", shopHeader);
            }

            var body = await new StreamReader(Request.Body).ReadToEndAsync();
            _logger.LogDebug("App uninstalled payload: {Body}", body);

            return Ok();
        }


        private async Task<bool> VerifyWebhookSignature()
        {
            Request.Headers.TryGetValue("X-Shopify-Hmac-Sha256", out var signature);
            
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            Request.Body.Position = 0;

            var webhookSecret = _configuration["Shopify:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret) || webhookSecret == "YOUR_WEBHOOK_SECRET")
            {
                _logger.LogWarning("Webhook secret not configured. Skipping signature validation for testing/development.");
                return true;
            }

            if (string.IsNullOrEmpty(signature))
            {
                return false;
            }

            return _oauthService.VerifyWebhookSignature(body, signature.ToString(), webhookSecret);
        }

        public static BookingRequest MapShopifyOrderToSkypointBooking(ShopifyOrderWebhook shopifyOrder, string skypointUserId)
        {
            var shippingAddress = shopifyOrder.shipping_address;
            var billingAddress = shopifyOrder.billing_address;
            var customer = shopifyOrder.customer;
            var pickupDate = shopifyOrder.created_at == default ? DateTime.UtcNow : shopifyOrder.created_at;
            var customerEmail = FirstNonEmpty(customer?.email, shopifyOrder.email);
            var dropOffPhone = FirstNonEmpty(shippingAddress?.phone, customer?.phone, " ");
            var pickUpPhone = FirstNonEmpty(customer?.phone, shippingAddress?.phone, " ");
            var parcelType = "A4_Text_Book";
            var lineItems = shopifyOrder.line_items.Count > 0
                ? shopifyOrder.line_items
                : new List<ShopifyLineItem> { new() { sku = shopifyOrder.order_number, quantity = 1 } };

            return new BookingRequest
            {
                UserId = skypointUserId,
                PickUpAddress = FirstNonEmpty(billingAddress?.address1, " "),
                DropOffAddress = FirstNonEmpty(shippingAddress?.address1, " "),
                FromSuburb = FirstNonEmpty(billingAddress?.city, " "),
                ToSuburb = FirstNonEmpty(shippingAddress?.city, " "),
                PickUpPCode = FirstNonEmpty(billingAddress?.zip, " "),
                DropOffPCode = FirstNonEmpty(shippingAddress?.zip, " "),
                Comment = $"@PickUp: Shopify Order #{shopifyOrder.order_number} @DropOff: No comment",
                Province = FirstNonEmpty(billingAddress?.province, " "),
                DestinationProvince = FirstNonEmpty(shippingAddress?.province, " "),
                DropOff = new DropOffPerson
                {
                    FirstName = FirstNonEmpty(shippingAddress?.first_name, customer?.first_name, " "),
                    LastName = FirstNonEmpty(shippingAddress?.last_name, customer?.last_name, " "),
                    Phone = dropOffPhone,
                    Email = customerEmail,
                    Suburb = FirstNonEmpty(shippingAddress?.city, " "),
                    City = FirstNonEmpty(shippingAddress?.city, " "),
                    State = FirstNonEmpty(shippingAddress?.province, " "),
                    Zip = FirstNonEmpty(shippingAddress?.zip, " ")
                },
                PickUp = new PickUpPerson
                {
                    FirstName = FirstNonEmpty(billingAddress?.first_name, customer?.first_name, " "),
                    LastName = FirstNonEmpty(billingAddress?.last_name, customer?.last_name, " "),
                    Phone = pickUpPhone,
                    Email = customerEmail,
                    Suburb = FirstNonEmpty(billingAddress?.city, " "),
                    City = FirstNonEmpty(billingAddress?.city, " "),
                    State = FirstNonEmpty(billingAddress?.province, " "),
                    Zip = FirstNonEmpty(billingAddress?.zip, " ")
                },
                Type = "ROAD",
                PickUpDate = pickupDate.ToString("dd"),
                PickUpTime = pickupDate.ToString("HH:mm"),
                ParcelDimensions = lineItems.Select(item => new ParcelDimension
                {
                    ParcelMass = 5.0,
                    ParcelLength = 30.0,
                    ParcelBreadth = 30.0,
                    ParcelHeight = 23.0,
                    PredefinedParcel = parcelType,
                    ParcelReference = FirstNonEmpty(item.sku, item.title, shopifyOrder.order_number),
                    SelectedParcel = parcelType
                }).ToList(),
                PickUpCity = FirstNonEmpty(billingAddress?.city, " "),
                DropOffCity = FirstNonEmpty(shippingAddress?.city, " "),
                PickUpZip = FirstNonEmpty(billingAddress?.zip, " "),
                DropOffZip = FirstNonEmpty(shippingAddress?.zip, " "),
                ShipmentType = string.Empty,
                ToCounterCode = string.Empty,
                ToCounterName = string.Empty,
                SaIdNumber = string.Empty,
                PickUpCountry = FirstNonEmpty(billingAddress?.country, string.Empty)
            };
        }

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
