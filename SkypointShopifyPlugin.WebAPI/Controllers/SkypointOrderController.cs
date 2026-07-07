using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    /// <summary>
    /// Controller for Skypoint order management
    /// Provides endpoints for creating, processing, and managing Skypoint orders
    /// Mirrors the Shopify order management pattern
    /// </summary>
    [ApiController]
    [Route("api/skypoint/orders")]
    [Authorize]
    public class SkypointOrderController : ControllerBase
    {
        private readonly ILogger<SkypointOrderController> _logger;
        private readonly ISkypointOrderService _orderService;

        public SkypointOrderController(
            ILogger<SkypointOrderController> logger,
            ISkypointOrderService orderService)
        {
            _logger = logger;
            _orderService = orderService;
        }

        /// <summary>
        /// Create a new Skypoint order
        /// POST /api/skypoint/orders
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateSkypointOrderRequest request, [FromQuery] bool autoProcess = true)
        {
            _logger.LogInformation("Create order request for {OrderNumber}", request.OrderNumber);

            if (string.IsNullOrEmpty(request.OrderNumber))
            {
                return BadRequest(new { error = "Order number is required" });
            }

            var result = await _orderService.CreateOrderAsync(request, autoProcess);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return StatusCode(500, result);
            }
        }

        /// <summary>
        /// Process an existing order into a Skypoint booking
        /// POST /api/skypoint/orders/{orderId}/process
        /// </summary>
        [HttpPost("{orderId}/process")]
        public async Task<IActionResult> ProcessOrder(string orderId, [FromQuery] bool force = false)
        {
            _logger.LogInformation("Process order request for {OrderId} (force: {Force})", orderId, force);

            var result = await _orderService.ProcessOrderAsync(orderId, force);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return StatusCode(500, result);
            }
        }

        /// <summary>
        /// Get an order by ID
        /// GET /api/skypoint/orders/{orderId}
        /// </summary>
        [HttpGet("{orderId}")]
        public async Task<IActionResult> GetOrderById(string orderId)
        {
            _logger.LogInformation("Get order request for {OrderId}", orderId);

            var order = await _orderService.GetOrderByIdAsync(orderId);

            if (order == null)
            {
                return NotFound(new { error = "Order not found" });
            }

            return Ok(new { success = true, order });
        }

        /// <summary>
        /// Get an order by order number
        /// GET /api/skypoint/orders/number/{orderNumber}
        /// </summary>
        [HttpGet("number/{orderNumber}")]
        public async Task<IActionResult> GetOrderByNumber(string orderNumber)
        {
            _logger.LogInformation("Get order by number request for {OrderNumber}", orderNumber);

            var order = await _orderService.GetOrderByNumberAsync(orderNumber);

            if (order == null)
            {
                return NotFound(new { error = "Order not found" });
            }

            return Ok(new { success = true, order });
        }

        /// <summary>
        /// Get orders with filtering and pagination
        /// POST /api/skypoint/orders/list
        /// </summary>
        [HttpPost("list")]
        public async Task<IActionResult> GetOrders([FromBody] SkypointOrderFilter filter)
        {
            _logger.LogInformation("Get orders request with filter");

            var result = await _orderService.GetOrdersAsync(filter);

            return Ok(result);
        }

        /// <summary>
        /// Update order status
        /// PUT /api/skypoint/orders/{orderId}/status
        /// </summary>
        [HttpPut("{orderId}/status")]
        public async Task<IActionResult> UpdateOrderStatus(string orderId, [FromBody] UpdateStatusRequest request)
        {
            _logger.LogInformation("Update status request for order {OrderId} to {Status}", orderId, request.Status);

            if (string.IsNullOrEmpty(request.Status))
            {
                return BadRequest(new { error = "Status is required" });
            }

            var success = await _orderService.UpdateOrderStatusAsync(orderId, request.Status);

            if (success)
            {
                return Ok(new { success = true, message = "Status updated successfully" });
            }
            else
            {
                return StatusCode(500, new { success = false, message = "Failed to update status" });
            }
        }

        /// <summary>
        /// Update order with Skypoint booking details
        /// PUT /api/skypoint/orders/{orderId}/booking
        /// </summary>
        [HttpPut("{orderId}/booking")]
        public async Task<IActionResult> UpdateOrderWithBooking(string orderId, [FromBody] UpdateBookingRequest request)
        {
            _logger.LogInformation("Update booking details for order {OrderId}", orderId);

            var success = await _orderService.UpdateOrderWithBookingAsync(
                orderId,
                request.BookingId,
                request.TrackNo,
                request.Status);

            if (success)
            {
                return Ok(new { success = true, message = "Booking details updated successfully" });
            }
            else
            {
                return StatusCode(500, new { success = false, message = "Failed to update booking details" });
            }
        }

        /// <summary>
        /// Sync order status from Skypoint API
        /// POST /api/skypoint/orders/{orderId}/sync
        /// </summary>
        [HttpPost("{orderId}/sync")]
        public async Task<IActionResult> SyncOrderStatus(string orderId)
        {
            _logger.LogInformation("Sync status request for order {OrderId}", orderId);

            var success = await _orderService.SyncOrderStatusAsync(orderId);

            if (success)
            {
                return Ok(new { success = true, message = "Status synced successfully" });
            }
            else
            {
                return StatusCode(500, new { success = false, message = "Failed to sync status" });
            }
        }

        /// <summary>
        /// Delete an order
        /// DELETE /api/skypoint/orders/{orderId}
        /// </summary>
        [HttpDelete("{orderId}")]
        public async Task<IActionResult> DeleteOrder(string orderId)
        {
            _logger.LogInformation("Delete order request for {OrderId}", orderId);

            var success = await _orderService.DeleteOrderAsync(orderId);

            if (success)
            {
                return Ok(new { success = true, message = "Order deleted successfully" });
            }
            else
            {
                return StatusCode(500, new { success = false, message = "Failed to delete order" });
            }
        }

        /// <summary>
        /// Update order with PUDO details
        /// PUT /api/skypoint/orders/{orderId}/pudo
        /// </summary>
        [HttpPut("{orderId}/pudo")]
        public async Task<IActionResult> UpdateOrderPudo(string orderId, [FromBody] UpdateOrderPudoRequest request)
        {
            _logger.LogInformation("Update PUDO details request for order {OrderId}", orderId);

            if (string.IsNullOrEmpty(request.ToCounterCode))
            {
                return BadRequest(new { error = "ToCounterCode is required" });
            }

            var success = await _orderService.UpdateOrderPudoAsync(
                orderId,
                request.ToCounterCode,
                request.ToCounterName,
                request.PudoAddress1,
                request.PudoSuburb,
                request.PudoCity,
                request.PudoZip,
                request.PudoProvider);

            if (success)
            {
                return Ok(new { success = true, message = "PUDO details updated successfully" });
            }
            else
            {
                return StatusCode(500, new { success = false, message = "Failed to update PUDO details" });
            }
        }

        /// <summary>
        /// Download waybill PDF for an order
        /// GET /api/skypoint/orders/{orderId}/waybill/download
        /// </summary>
        [HttpGet("{orderId}/waybill/download")]
        public async Task<IActionResult> DownloadWaybill(string orderId)
        {
            _logger.LogInformation("Download waybill request for order {OrderId}", orderId);

            var result = await _orderService.DownloadWaybillAsync(orderId);

            if (result != null)
            {
                return Ok(result);
            }
            else
            {
                return NotFound(new { success = false, message = "Waybill not found or download failed" });
            }
        }
    }

    /// <summary>
    /// Request DTO for updating order status
    /// </summary>
    public class UpdateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for updating order with booking details
    /// </summary>
    public class UpdateBookingRequest
    {
        public string BookingId { get; set; } = string.Empty;
        public string TrackNo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
