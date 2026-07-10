using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    /// <summary>
    /// Controller for Skypoint order management
    /// </summary>
    [ApiController]
    [Route("api/skypoint/orders")]
    [Authorize]
    public class SkypointOrderController : ControllerBase
    {
        private readonly ILogger<SkypointOrderController> _logger;
        private readonly ISkypointOrderService _orderService;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly ISkypointTokenStore _skypointTokenStore;

        public SkypointOrderController(
            ILogger<SkypointOrderController> logger,
            ISkypointOrderService orderService,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore)
        {
            _logger = logger;
            _orderService = orderService;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
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

            try
            {
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
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading waybill for order {OrderId}", orderId);
                return StatusCode(500, new { success = false, message = "An error occurred while downloading the waybill" });
            }
        }
        /// <summary>
        /// Proxy bulk label print request to Skypoint API
        /// POST /api/skypoint/orders/bulk/label
        /// </summary>
        [HttpPost("bulk/label")]
        public async Task<IActionResult> BulkLabelPrint([FromBody] List<string> bookingIds)
        {
            if (bookingIds == null || bookingIds.Count == 0)
                return BadRequest(new { success = false, message = "No booking IDs provided." });

            _logger.LogInformation("Bulk label print requested for {Count} booking(s)", bookingIds.Count);

            try
            {
                string? skypointToken = null;

                // 1. Try skypoint_token embedded in JWT claim (set at login)
                var jwtSkypointToken = User.FindFirst("skypoint_token")?.Value;
                if (!string.IsNullOrEmpty(jwtSkypointToken))
                {
                    skypointToken = jwtSkypointToken;
                    _logger.LogInformation("Using Skypoint token from JWT claim for bulk label");
                }

                // 2. Try X-Skypoint-Token header (forwarded directly from the UI session)
                if (string.IsNullOrEmpty(skypointToken))
                {
                    var headerToken = Request.Headers["X-Skypoint-Token"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(headerToken))
                    {
                        skypointToken = headerToken;
                        _logger.LogInformation("Using Skypoint token from X-Skypoint-Token header for bulk label");
                    }
                }

                // 3. Fall back to token store keyed by shop claim in JWT
                if (string.IsNullOrEmpty(skypointToken))
                {
                    var shopClaim = User.FindFirst("shop")?.Value;
                    var vid = !string.IsNullOrEmpty(shopClaim) ? shopClaim : "default";

                    skypointToken = _skypointTokenStore.GetToken(vid);

                    // 4. If still missing, try re-auth using stored credentials
                    if (string.IsNullOrEmpty(skypointToken))
                    {
                        var creds = _skypointTokenStore.GetCredentials(vid);
                        if (creds != null)
                        {
                            var loginResp = await _skypointApiClient.LoginAsync(new Core.DTOs.Skypoint.LoginRequest
                            {
                                Username = creds.Value.username,
                                Pwd      = creds.Value.password
                            });
                            if (loginResp?.Token?.TokenValue != null)
                            {
                                _skypointTokenStore.SaveToken(vid, loginResp.Token.TokenValue, loginResp.Token.Expiration, loginResp.Id);
                                skypointToken = loginResp.Token.TokenValue;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(skypointToken))
                    return Unauthorized(new { success = false, message = "No Skypoint token available. Please log in again." });

                var result = await _skypointApiClient.BulkLabelPrintAsync(bookingIds, skypointToken);

                if (result == null || string.IsNullOrEmpty(result.FileStream))
                    return NotFound(new { success = false, message = "Bulk label print returned no file data." });

                return Ok(result);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Bulk label print API call failed");
                return StatusCode(502, new { success = false, message = $"Skypoint API error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during bulk label print");
                return StatusCode(500, new { success = false, message = "An unexpected error occurred." });
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
