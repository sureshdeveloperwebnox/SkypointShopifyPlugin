using MediatR;
using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Application.Common;
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

        public ShopifyController(IMediator mediator, ILogger<ShopifyController> logger, IShopifyOAuthService oauthService, IConfiguration configuration)
        {
            _mediator = mediator;
            _logger = logger;
            _oauthService = oauthService;
            _configuration = configuration;
        }

        /// <summary>
        /// Default endpoint - Shopify store requesting to install app
        /// </summary>
        [HttpGet]
        public IActionResult Default(string shop)
        {
            _logger.LogInformation("Install request from shop: {Shop}", shop);
            
            try
            {
                var installUrl = _oauthService.GetInstallUrl(shop);
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
        /// OAuth callback - Shopify redirects here after user approves
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

                var accessToken = await _oauthService.ExchangeCodeForAccessTokenAsync(shop, code);
                
                // TODO: Store access token securely (database)
                _logger.LogInformation("Successfully obtained access token for shop: {Shop}", shop);
                
                return Ok(new { 
                    message = "App installed successfully", 
                    shop,
                    token_received = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exchanging code for access token for shop: {Shop}", shop);
                return StatusCode(500, new { error = "Failed to complete installation" });
            }
        }

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

            // TODO: Process order and create Skypoint booking
            var body = await new StreamReader(Request.Body).ReadToEndAsync();
            _logger.LogInformation("Order create payload: {Body}", body);
            
            return Ok();
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
    }
}
