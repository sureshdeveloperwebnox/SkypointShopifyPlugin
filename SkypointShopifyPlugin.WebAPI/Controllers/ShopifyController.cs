using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Application.Common;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.WebAPI.Filters;
using Microsoft.AspNetCore.Authorization;
using SkypointShopifyPlugin.Infrastructure.Services;
using SkypointShopifyPlugin.Core.Common;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/shopify")]
    public class ShopifyController : ControllerBase
    {
        private static IWebhookQueue? _staticQueueInstance;
        public static System.Collections.Concurrent.ConcurrentDictionary<string, BookingResponse> ProcessedOrderBookings => 
            _staticQueueInstance?.ProcessedOrderBookings ?? new System.Collections.Concurrent.ConcurrentDictionary<string, BookingResponse>(StringComparer.OrdinalIgnoreCase);

        private readonly IMediator _mediator;
        private readonly ILogger<ShopifyController> _logger;
        private readonly IShopifyOAuthService _oauthService;
        private readonly IConfiguration _configuration;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IShopTokenStore _shopTokenStore;
        private readonly ISkypointTokenStore _skypointTokenStore;
        private readonly IWebhookQueue _webhookQueue;

        public ShopifyController(
            IMediator mediator, 
            ILogger<ShopifyController> logger, 
            IShopifyOAuthService oauthService, 
            IConfiguration configuration, 
            ISkypointApiClient skypointApiClient, 
            IShopifyAdminService shopifyAdminService, 
            IShopTokenStore shopTokenStore, 
            ISkypointTokenStore skypointTokenStore,
            IWebhookQueue webhookQueue)
        {
            _mediator = mediator;
            _logger = logger;
            _oauthService = oauthService;
            _configuration = configuration;
            _skypointApiClient = skypointApiClient;
            _shopifyAdminService = shopifyAdminService;
            _shopTokenStore = shopTokenStore;
            _skypointTokenStore = skypointTokenStore;
            _webhookQueue = webhookQueue;
            _staticQueueInstance = webhookQueue;
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
            {
                var webUiUrl = _configuration["WebUi:BaseUrl"]?.TrimEnd('/');
                if (!string.IsNullOrEmpty(webUiUrl))
                {
                    return Redirect($"{webUiUrl}/login?v={DateTime.UtcNow.Ticks}");
                }
                return Redirect($"/login?v={DateTime.UtcNow.Ticks}");
            }

            shop = NormalizeShopDomain(shop);
            try
            {
                var redirectUri = BuildRedirectUri();
                var installUrl = _oauthService.GetInstallUrl(shop, redirectUri);
                return RedirectTop(installUrl);
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
                var tokenResponse = await _oauthService.ExchangeCodeForAccessTokenAsync(shop, code);
                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.access_token))
                    return StatusCode(500, new { error = "Failed to obtain access token" });

                var accessToken = tokenResponse.access_token;

                // Persist token immediately — in-memory cache
                _shopTokenStore.SaveToken(shop, accessToken, tokenResponse.refresh_token, tokenResponse.expires_in);

                // Register carrier service and webhooks in background — don't block the redirect.
                // Build carrier URL from the public ngrok/production base URL.
                var publicBase = BuildPublicBaseUrl();
                var carrierUrl = $"{publicBase}/api/carrier/rates?shop={Uri.EscapeDataString(shop)}";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Register metafield definitions
                        await _shopifyAdminService.RegisterMetafieldDefinitionsAsync(shop, accessToken);
                        _logger.LogInformation("Metafields registered automatically for {Shop}", shop);

                        // Populate default settings values
                        await _shopifyAdminService.PopulateDefaultSettingsAsync(shop, accessToken);
                        _logger.LogInformation("Default metafield values populated automatically for {Shop}", shop);

                        // Register carrier service
                        await _shopifyAdminService.RegisterAndAssignCarrierServiceAsync(shop, accessToken, carrierUrl);
                        
                        // Register webhooks automatically
                        await _shopifyAdminService.SyncWebhooksAsync(shop, accessToken, publicBase);
                        _logger.LogInformation("Webhooks registered automatically for {Shop}", shop);

                        // Register script tags automatically
                        await _shopifyAdminService.SyncScriptTagsAsync(shop, accessToken, publicBase);
                        _logger.LogInformation("Script tags registered automatically for {Shop}", shop);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background registration failed for {Shop} — will retry on next dashboard open", shop);
                    }
                });

                // Redirect to app UI
                var webUiUrl = _configuration["WebUi:BaseUrl"]?.TrimEnd('/');
                if (!string.IsNullOrEmpty(webUiUrl))
                {
                    return Redirect($"{webUiUrl}/login?shop={Uri.EscapeDataString(shop)}&v={DateTime.UtcNow.Ticks}");
                }
                return Redirect($"/login?shop={Uri.EscapeDataString(shop)}&v={DateTime.UtcNow.Ticks}");
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
        [Authorize]
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

            // Register metafield definitions
            try
            {
                await _shopifyAdminService.RegisterMetafieldDefinitionsAsync(shop, accessToken);
                _logger.LogInformation("Metafields registered/synced for {Shop}", shop);

                await _shopifyAdminService.PopulateDefaultSettingsAsync(shop, accessToken);
                _logger.LogInformation("Default metafield values populated automatically for {Shop}", shop);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metafields registration failed for {Shop}", shop);
            }

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

            // Register script tags automatically
            try
            {
                await _shopifyAdminService.SyncScriptTagsAsync(shop, accessToken, publicBase);
                _logger.LogInformation("Script tags synced for {Shop}", shop);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Script tag sync failed for {Shop}", shop);
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
        {
            if (string.IsNullOrEmpty(shop)) return "";
            var normalized = shop.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                                 .Trim()
                                 .TrimEnd('/')
                                 .ToLowerInvariant();
            if (normalized.EndsWith(".myshopify.co", StringComparison.OrdinalIgnoreCase))
            {
                normalized += "m";
            }
            return normalized;
        }

        /// <summary>
        /// Webhook endpoint for orders/create
        /// </summary>
        [HttpPost("orders/create")]
        [ShopifyHmacValidation]
        public async Task<IActionResult> OrdersCreate()
        {
            _logger.LogInformation("Order create webhook received");
            
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                
                var shop = Request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault();
                if (string.IsNullOrEmpty(shop))
                {
                    _logger.LogError("Webhook headers missing shop domain");
                    return BadRequest(new { error = "Missing shop domain header" });
                }
                shop = NormalizeShopDomain(shop);

                await _webhookQueue.QueueWebhookAsync(new WebhookJob
                {
                    ShopDomain = shop,
                    Topic = "orders/create",
                    Payload = body
                });

                return Accepted(new { message = "Webhook received and queued for background processing" });
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
        [ShopifyHmacValidation]
        public async Task<IActionResult> OrdersUpdated()
        {
            _logger.LogInformation("Order updated webhook received");
            
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                
                var shop = Request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault();
                if (string.IsNullOrEmpty(shop))
                {
                    _logger.LogError("Webhook headers missing shop domain");
                    return BadRequest(new { error = "Missing shop domain header" });
                }
                shop = NormalizeShopDomain(shop);

                await _webhookQueue.QueueWebhookAsync(new WebhookJob
                {
                    ShopDomain = shop,
                    Topic = "orders/updated",
                    Payload = body
                });

                return Accepted(new { message = "Webhook received and queued for background processing" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order updated webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Webhook endpoint for orders/cancelled
        /// </summary>
        [HttpPost("orders/cancelled")]
        [ShopifyHmacValidation]
        public async Task<IActionResult> OrdersCancelled()
        {
            _logger.LogInformation("Order cancelled webhook received");
            
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                
                var shop = Request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault();
                if (string.IsNullOrEmpty(shop))
                {
                    _logger.LogError("Webhook headers missing shop domain");
                    return BadRequest(new { error = "Missing shop domain header" });
                }
                shop = NormalizeShopDomain(shop);

                await _webhookQueue.QueueWebhookAsync(new WebhookJob
                {
                    ShopDomain = shop,
                    Topic = "orders/cancelled",
                    Payload = body
                });

                return Accepted(new { message = "Webhook received and queued for background processing" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order cancelled webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Webhook endpoint for app/uninstalled — removes the stored OAuth token for the shop.
        /// </summary>
        [HttpPost("app/uninstalled")]
        [ShopifyHmacValidation]
        public async Task<IActionResult> AppUninstalled()
        {
            _logger.LogInformation("App uninstalled webhook received");

            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                
                var shop = Request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault();
                if (string.IsNullOrEmpty(shop))
                {
                    _logger.LogError("Webhook headers missing shop domain");
                    return BadRequest(new { error = "Missing shop domain header" });
                }
                shop = NormalizeShopDomain(shop);

                await _webhookQueue.QueueWebhookAsync(new WebhookJob
                {
                    ShopDomain = shop,
                    Topic = "app/uninstalled",
                    Payload = body
                });

                return Accepted(new { message = "Webhook received and queued for background processing" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing app uninstalled webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// GDPR Webhook: customers/redact
        /// Called when a store owner requests deletion of customer personal data.
        /// </summary>
        [HttpPost("gdpr/customers/redact")]
        [ShopifyHmacValidation]
        public IActionResult CustomersRedact()
        {
            _logger.LogInformation(LogEventIds.WebhookReceived, "Shopify GDPR customers/redact request received.");
            // Since this is a stateless/database-free plugin for customer records, we have no customer data to erase.
            return Ok();
        }

        /// <summary>
        /// GDPR Webhook: customers/data_request
        /// Called when a customer requests a copy of their stored personal data.
        /// </summary>
        [HttpPost("gdpr/customers/data_request")]
        [ShopifyHmacValidation]
        public IActionResult CustomersDataRequest()
        {
            _logger.LogInformation(LogEventIds.WebhookReceived, "Shopify GDPR customers/data_request request received.");
            // Return empty/standard acknowledgement since we do not store customer records.
            return Ok();
        }

        /// <summary>
        /// GDPR Webhook: shop/redact
        /// Called when a store owner deletes their store or uninstalls the app.
        /// </summary>
        [HttpPost("gdpr/shop/redact")]
        [ShopifyHmacValidation]
        public IActionResult ShopRedact()
        {
            _logger.LogInformation(LogEventIds.WebhookReceived, "Shopify GDPR shop/redact request received.");
            // Store token/settings cleanup is handled via the app/uninstalled webhook, so we just acknowledge here.
            return Ok();
        }

        public static BookingRequest MapShopifyOrderToSkypointBooking(ShopifyOrderWebhook shopifyOrder, string skypointUserId, IConfiguration configuration)
        {
            return SkypointOrderMapper.MapShopifyOrderToSkypointBooking(shopifyOrder, skypointUserId, configuration);
        }

        private ContentResult RedirectTop(string url)
        {
            var html = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Redirecting...</title>
                    <script type='text/javascript'>
                        window.top.location.href = '{url}';
                    </script>
                </head>
                <body>
                    <p>Redirecting to authorize... If you are not redirected, <a href='{url}' target='_top'>click here</a>.</p>
                </body>
                </html>";
            return Content(html, "text/html");
        }
    }
}
