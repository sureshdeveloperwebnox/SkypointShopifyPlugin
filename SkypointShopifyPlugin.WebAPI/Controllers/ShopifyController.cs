using MediatR;
using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Application.Common;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/shopify")]
    public class ShopifyController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly ILogger<ShopifyController> _logger;

        public ShopifyController(IMediator mediator, ILogger<ShopifyController> logger)
        {
            _mediator = mediator;
            _logger = logger;
        }

        /// <summary>
        /// Default endpoint - Shopify store requesting to install app
        /// </summary>
        [HttpGet]
        public IActionResult Default(string shop)
        {
            _logger.LogInformation("Install request from shop: {Shop}", shop);
            // TODO: Implement Shopify OAuth flow
            return Ok(new { message = "Shopify install endpoint", shop });
        }

        /// <summary>
        /// OAuth callback - Shopify redirects here after user approves
        /// </summary>
        [HttpGet("auth")]
        public IActionResult Auth(string shop, string code, string state)
        {
            _logger.LogInformation("Auth callback from shop: {Shop}", shop);
            // TODO: Implement OAuth token exchange
            return Ok(new { message = "Shopify auth callback", shop });
        }

        /// <summary>
        /// Webhook endpoint for orders/create
        /// </summary>
        [HttpPost("orders/create")]
        public IActionResult OrdersCreate()
        {
            _logger.LogInformation("Order create webhook received");
            // TODO: Process order and create Skypoint booking
            return Ok();
        }

        /// <summary>
        /// Webhook endpoint for orders/updated
        /// </summary>
        [HttpPost("orders/updated")]
        public IActionResult OrdersUpdated()
        {
            _logger.LogInformation("Order updated webhook received");
            // TODO: Process order update
            return Ok();
        }

        /// <summary>
        /// Webhook endpoint for orders/cancelled
        /// </summary>
        [HttpPost("orders/cancelled")]
        public IActionResult OrdersCancelled()
        {
            _logger.LogInformation("Order cancelled webhook received");
            // TODO: Process order cancellation
            return Ok();
        }

        /// <summary>
        /// Webhook endpoint for app/uninstalled
        /// </summary>
        [HttpPost("app/uninstalled")]
        public IActionResult AppUninstalled()
        {
            _logger.LogInformation("App uninstalled webhook received");
            // TODO: Clean up shop data
            return Ok();
        }
    }
}
