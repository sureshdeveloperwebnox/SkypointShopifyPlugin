using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/shopify/order-actions")]
    public class ShopifyOrderActionController : ControllerBase
    {
        private readonly ILogger<ShopifyOrderActionController> _logger;
        private readonly ISkypointOrderService _orderService;
        private readonly ShopifySettings _shopifySettings;

        public ShopifyOrderActionController(
            ILogger<ShopifyOrderActionController> logger,
            ISkypointOrderService orderService,
            IOptions<ShopifySettings> shopifySettings)
        {
            _logger = logger;
            _orderService = orderService;
            _shopifySettings = shopifySettings.Value;
        }

        private string? ValidateShopifySessionToken(string authHeader, out string? shopDomain)
        {
            shopDomain = null;
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return "Missing or invalid authorization header.";
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);
                
                var clientId = jwtToken.Audiences.FirstOrDefault();
                if (string.IsNullOrEmpty(clientId))
                {
                    return "No audience (client ID) claim in token.";
                }

                // Symmetrically verify using our Client Secret
                var clientSecret = _shopifySettings.ClientSecret;

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clientSecret)),
                    ValidateIssuer = false,
                    ValidateAudience = true,
                    ValidAudience = clientId,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
                var destClaim = principal.FindFirst("dest")?.Value;
                if (string.IsNullOrEmpty(destClaim))
                {
                    return "Missing 'dest' claim in session token.";
                }

                // Normalize shop domain from dest claim (e.g. https://teststore-hzegetac.myshopify.com -> teststore-hzegetac.myshopify.com)
                shopDomain = destClaim.Replace("https://", "").Replace("http://", "").Trim().ToLowerInvariant().TrimEnd('/');
                return null; // success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shopify Session Token validation failed");
                return $"Token validation failed: {ex.Message}";
            }
        }

        [HttpGet("details")]
        public async Task<IActionResult> GetDetails([FromQuery] string orderId)
        {
            _logger.LogInformation("GetDetails action requested for order {OrderId}", orderId);
            
            var authHeader = Request.Headers["Authorization"].ToString();
            var error = ValidateShopifySessionToken(authHeader, out var shopDomain);
            if (error != null)
            {
                return Unauthorized(new { success = false, message = error });
            }

            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                return NotFound(new { success = false, message = "No SkyPoint booking log found for this Shopify order." });
            }

            return Ok(new { success = true, order });
        }

        [HttpPost("pay")]
        public async Task<IActionResult> PayOrder([FromQuery] string orderId)
        {
            _logger.LogInformation("PayOrder action requested for order {OrderId}", orderId);

            var authHeader = Request.Headers["Authorization"].ToString();
            var error = ValidateShopifySessionToken(authHeader, out var shopDomain);
            if (error != null)
            {
                return Unauthorized(new { success = false, message = error });
            }

            var result = await _orderService.PayOrderWithWalletAsync(orderId);
            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return StatusCode(500, result);
            }
        }

        [HttpPost("sync")]
        public async Task<IActionResult> SyncOrderStatus([FromQuery] string orderId)
        {
            _logger.LogInformation("SyncOrderStatus action requested for order {OrderId}", orderId);

            var authHeader = Request.Headers["Authorization"].ToString();
            var error = ValidateShopifySessionToken(authHeader, out var shopDomain);
            if (error != null)
            {
                return Unauthorized(new { success = false, message = error });
            }

            var success = await _orderService.SyncOrderStatusAsync(orderId);
            if (success)
            {
                var order = await _orderService.GetOrderByIdAsync(orderId);
                return Ok(new { success = true, message = "Status synced successfully", order });
            }
            else
            {
                return StatusCode(500, new { success = false, message = "Failed to sync status" });
            }
        }

        [HttpGet("waybill/download")]
        public async Task<IActionResult> DownloadWaybill([FromQuery] string orderId)
        {
            _logger.LogInformation("DownloadWaybill action requested for order {OrderId}", orderId);

            var authHeader = Request.Headers["Authorization"].ToString();
            var error = ValidateShopifySessionToken(authHeader, out var shopDomain);
            if (error != null)
            {
                return Unauthorized(new { success = false, message = error });
            }

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
                _logger.LogError(ex, "Error executing DownloadWaybill for order {OrderId}", orderId);
                return StatusCode(500, new { success = false, message = "An error occurred while downloading the waybill" });
            }
        }

        [HttpGet("waybill/download-pdf")]
        public async Task<IActionResult> DownloadWaybillPdf([FromQuery] string orderId, [FromQuery] string token)
        {
            _logger.LogInformation("DownloadWaybillPdf endpoint requested for order {OrderId}", orderId);

            var error = ValidateShopifySessionToken("Bearer " + token, out var shopDomain);
            if (error != null)
            {
                return Unauthorized(new { success = false, message = error });
            }

            try
            {
                var result = await _orderService.DownloadWaybillAsync(orderId);
                if (result != null && !string.IsNullOrEmpty(result.FileStream))
                {
                    var pdfBytes = Convert.FromBase64String(result.FileStream);
                    var fileName = result.FileName ?? $"waybill_{orderId}.pdf";
                    var mimeType = result.ApplicationType ?? "application/pdf";
                    return File(pdfBytes, mimeType, fileName);
                }
                else
                {
                    return NotFound("Waybill not found or download failed");
                }
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing DownloadWaybillPdf for order {OrderId}", orderId);
                return StatusCode(500, "An error occurred while downloading the waybill PDF");
            }
        }

        [HttpGet("test-track")]
        public async Task<IActionResult> TestTrack([FromQuery] string? trackNo, [FromQuery] string? bookingId, [FromQuery] string? waybillNo)
        {
            var apiClient = HttpContext.RequestServices.GetRequiredService<ISkypointApiClient>();
            var tokenStore = HttpContext.RequestServices.GetRequiredService<ISkypointTokenStore>();
            var vendorId = "teststore-hzegetac.myshopify.com";
            var token = tokenStore.GetToken(vendorId);
            
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest("No token found for test store");
            }

            try
            {
                object? tracking = null;
                object? details = null;
                object? download = null;

                if (!string.IsNullOrEmpty(trackNo))
                {
                    tracking = await apiClient.TrackBookingAsync(trackNo, token);
                }

                if (!string.IsNullOrEmpty(bookingId))
                {
                    details = await apiClient.GetBookingDetailsAsync(bookingId, token);
                }

                if (!string.IsNullOrEmpty(waybillNo))
                {
                    try
                    {
                        download = await apiClient.DownloadWaybillAsync(waybillNo, token);
                    }
                    catch (Exception ex)
                    {
                        download = new { error = ex.Message };
                    }
                }

                return Ok(new { tracking, details, download });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
