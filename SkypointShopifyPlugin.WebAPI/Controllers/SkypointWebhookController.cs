using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/skypoint/webhooks")]
    public class SkypointWebhookController : ControllerBase
    {
        private readonly ILogger<SkypointWebhookController> _logger;
        private readonly ISkypointOrderService _orderService;

        public SkypointWebhookController(
            ILogger<SkypointWebhookController> logger,
            ISkypointOrderService orderService)
        {
            _logger = logger;
            _orderService = orderService;
        }

        /// <summary>
        /// Real-time push webhook receiver for SkyPoint shipment tracking status updates.
        /// POST /api/skypoint/webhooks/tracking
        /// </summary>
        [HttpPost("tracking")]
        public async Task<IActionResult> HandleTrackingWebhook([FromBody] SkypointTrackingWebhookPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.TrackNo))
            {
                return BadRequest(new { error = "trackNo is required in request body" });
            }

            _logger.LogInformation("SkyPoint push tracking webhook received for waybill: {TrackNo}", payload.TrackNo);

            try
            {
                // Find order matching the trackNo
                var listResponse = await _orderService.GetOrdersAsync(new SkypointOrderFilter { Limit = 5000 });
                var order = listResponse.Orders.FirstOrDefault(o => 
                    payload.TrackNo.Equals(o.SkypointTrackNo, StringComparison.OrdinalIgnoreCase) || 
                    payload.TrackNo.Equals(o.SkypointBookingId, StringComparison.OrdinalIgnoreCase));

                if (order == null)
                {
                    _logger.LogWarning("Order not found for trackNo: {TrackNo} in webhook processing", payload.TrackNo);
                    return NotFound(new { error = $"Order matching trackNo '{payload.TrackNo}' not found" });
                }

                // Call order service status sync (which pulls full event history, saves locally, and updates Shopify)
                var success = await _orderService.SyncOrderStatusAsync(order.Id);

                if (success)
                {
                    return Ok(new { success = true, message = "Tracking status synced successfully" });
                }
                else
                {
                    return StatusCode(500, new { error = "Failed to sync order tracking details" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SkyPoint tracking webhook for waybill {TrackNo}", payload.TrackNo);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }
    }

    public class SkypointTrackingWebhookPayload
    {
        public string TrackNo { get; set; } = string.Empty;
    }
}
