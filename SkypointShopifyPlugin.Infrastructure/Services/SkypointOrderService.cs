using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Service for managing Skypoint orders
    /// Handles order creation, processing, and lifecycle management
    /// Mirrors the order processing logic from Shopify integration
    /// </summary>
    public class SkypointOrderService : ISkypointOrderService
    {
        private readonly ILogger<SkypointOrderService> _logger;
        private readonly ISkypointOrderStore _orderStore;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly ISkypointTokenStore _skypointTokenStore;
        private readonly IEcommercePlatformService _ecommercePlatformService;
        private readonly IConfiguration _configuration;

        public SkypointOrderService(
            ILogger<SkypointOrderService> logger,
            ISkypointOrderStore orderStore,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore,
            IEcommercePlatformService ecommercePlatformService,
            IConfiguration configuration)
        {
            _logger = logger;
            _orderStore = orderStore;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
            _ecommercePlatformService = ecommercePlatformService;
            _configuration = configuration;
        }

        public async Task<SkypointOrderResponse> CreateOrderAsync(CreateSkypointOrderRequest request, bool autoProcess = true)
        {
            try
            {
                _logger.LogInformation("Creating Skypoint order {OrderNumber}", request.OrderNumber);

                // Check if order already exists
                var existingOrder = await _orderStore.GetOrderByNumberAsync(request.OrderNumber);
                if (existingOrder != null)
                {
                    _logger.LogWarning("Order {OrderNumber} already exists with ID {OrderId}", request.OrderNumber, existingOrder.Id);
                    return new SkypointOrderResponse
                    {
                        Success = false,
                        Message = $"Order {request.OrderNumber} already exists",
                        Order = existingOrder
                    };
                }

                // Map request to order
                var order = SkypointOrderMapper.MapToSkypointOrder(request);

                // Save order
                await _orderStore.SaveOrderAsync(order);
                _logger.LogInformation("Saved order {OrderId} with number {OrderNumber}", order.Id, order.OrderNumber);

                // Auto-process if requested
                if (autoProcess)
                {
                    var processResult = await ProcessOrderAsync(order.Id);
                    if (processResult.Success)
                    {
                        return processResult;
                    }
                    else
                    {
                        // Return order even if processing failed
                        return new SkypointOrderResponse
                        {
                            Success = true,
                            Message = $"Order created but processing failed: {processResult.Message}",
                            Order = order
                        };
                    }
                }

                return new SkypointOrderResponse
                {
                    Success = true,
                    Message = "Order created successfully",
                    Order = order
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Skypoint order {OrderNumber}", request.OrderNumber);
                return new SkypointOrderResponse
                {
                    Success = false,
                    Message = $"Error creating order: {ex.Message}"
                };
            }
        }

        public async Task<SkypointOrderResponse> ProcessOrderAsync(string orderId, bool force = false)
        {
            try
            {
                _logger.LogInformation("Processing order {OrderId} (force: {Force})", orderId, force);

                // Get order
                var order = await _orderStore.GetOrderByIdAsync(orderId);
                if (order == null)
                {
                    _logger.LogError("Order {OrderId} not found", orderId);
                    return new SkypointOrderResponse
                    {
                        Success = false,
                        Message = "Order not found"
                    };
                }

                // Check if already processed (unless force-processing is requested)
                if (!force && await _orderStore.IsOrderProcessedAsync(orderId))
                {
                    _logger.LogInformation("Order {OrderId} already processed with booking {BookingId}", orderId, order.SkypointBookingId);
                    return new SkypointOrderResponse
                    {
                        Success = true,
                        Message = "Order already processed",
                        Order = order,
                        SkypointBookingId = order.SkypointBookingId,
                        SkypointTrackNo = order.SkypointTrackNo
                    };
                }

                // Get Skypoint token and user ID
                var vendorId = order.VendorId ?? "default";
                var (token, skypointUserId) = await GetOrRefreshTokenAsync(vendorId);

                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(skypointUserId))
                {
                    _logger.LogError("No Skypoint credentials/token for vendor {VendorId}", vendorId);
                    return new SkypointOrderResponse
                    {
                        Success = false,
                        Message = "Skypoint credentials not configured or expired",
                        Order = order
                    };
                }

                // Map order to booking request
                var bookingRequest = SkypointOrderMapper.MapToBookingRequest(order, skypointUserId, _configuration);

                // Create booking
                var bookingResponse = await _skypointApiClient.CreateBookingAsync(bookingRequest, token);

                if (bookingResponse != null)
                {
                    // Update order with booking details
                    SkypointOrderMapper.UpdateWithBookingResponse(order, bookingResponse);
                    await _orderStore.UpdateOrderAsync(order);

                    _logger.LogInformation(
                        "Successfully created Skypoint booking {TrackNo} for order {OrderId}",
                        bookingResponse.TrackNo,
                        orderId);

                    return new SkypointOrderResponse
                    {
                        Success = true,
                        Message = "Order processed successfully",
                        Order = order,
                        SkypointBookingId = bookingResponse.Id,
                        SkypointTrackNo = bookingResponse.TrackNo
                    };
                }
                else
                {
                    _logger.LogError("Failed to create Skypoint booking for order {OrderId}", orderId);
                    return new SkypointOrderResponse
                    {
                        Success = false,
                        Message = "Failed to create Skypoint booking",
                        Order = order
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order {OrderId}", orderId);
                return new SkypointOrderResponse
                {
                    Success = false,
                    Message = $"Error processing order: {ex.Message}"
                };
            }
        }

        public async Task<SkypointOrder?> GetOrderByIdAsync(string orderId)
        {
            return await _orderStore.GetOrderByIdAsync(orderId);
        }

        public async Task<SkypointOrder?> GetOrderByNumberAsync(string orderNumber)
        {
            return await _orderStore.GetOrderByNumberAsync(orderNumber);
        }

        public async Task<SkypointOrderListResponse> GetOrdersAsync(SkypointOrderFilter filter)
        {
            try
            {
                var orders = await _orderStore.GetOrdersAsync(filter);
                var total = await _orderStore.GetOrderCountAsync(filter);

                return new SkypointOrderListResponse
                {
                    Success = true,
                    Orders = orders,
                    Pagination = new PaginationInfo
                    {
                        Page = filter.Page,
                        Limit = filter.Limit,
                        Total = total,
                        TotalPages = (int)Math.Ceiling((double)total / filter.Limit)
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting orders with filter");
                return new SkypointOrderListResponse
                {
                    Success = false,
                    Orders = new List<SkypointOrder>()
                };
            }
        }

        public async Task<bool> UpdateOrderStatusAsync(string orderId, string status)
        {
            try
            {
                var order = await _orderStore.GetOrderByIdAsync(orderId);
                if (order == null)
                {
                    _logger.LogError("Order {OrderId} not found for status update", orderId);
                    return false;
                }

                order.Status = status;
                order.UpdatedAt = DateTime.UtcNow;
                await _orderStore.UpdateOrderAsync(order);

                _logger.LogInformation("Updated order {OrderId} status to {Status}", orderId, status);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order {OrderId} status", orderId);
                return false;
            }
        }

        public async Task<bool> UpdateOrderWithBookingAsync(string orderId, string bookingId, string trackNo, string status)
        {
            try
            {
                await _orderStore.MarkOrderAsProcessedAsync(orderId, bookingId, trackNo, status);
                _logger.LogInformation("Updated order {OrderId} with booking {BookingId}", orderId, bookingId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order {OrderId} with booking details", orderId);
                return false;
            }
        }

        public async Task<bool> DeleteOrderAsync(string orderId)
        {
            try
            {
                await _orderStore.DeleteOrderAsync(orderId);
                _logger.LogInformation("Deleted order {OrderId}", orderId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order {OrderId}", orderId);
                return false;
            }
        }

        public async Task<bool> SyncOrderStatusAsync(string orderId)
        {
            try
            {
                var order = await _orderStore.GetOrderByIdAsync(orderId);
                if (order == null || (string.IsNullOrEmpty(order.SkypointTrackNo) && string.IsNullOrEmpty(order.SkypointBookingId)))
                {
                    _logger.LogWarning("Cannot sync status for order {OrderId} - not found, or has no booking ID or track number", orderId);
                    return false;
                }

                var trackNo = !string.IsNullOrEmpty(order.SkypointTrackNo) ? order.SkypointTrackNo : order.SkypointBookingId!;
                var vendorId = order.VendorId ?? "default";
                var (token, _) = await GetOrRefreshTokenAsync(vendorId);

                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogError("SkyPoint API credentials/token not configured or expired for vendor {VendorId} when syncing order {OrderId}", vendorId, orderId);
                    return false;
                }

                // Query tracking info from SkyPoint
                var trackingResponse = await _skypointApiClient.TrackBookingAsync(trackNo, token);
                if (trackingResponse == null || trackingResponse.TrackingInfo == null || trackingResponse.TrackingInfo.Count == 0)
                {
                    _logger.LogWarning("No tracking events found for waybill {TrackNo} of order {OrderId}", trackNo, orderId);
                    order.UpdatedAt = DateTime.UtcNow;
                    await _orderStore.UpdateOrderAsync(order);
                    return true;
                }

                // Update order tracking history
                order.TrackingHistory = trackingResponse.TrackingInfo;

                // Sync the waybill number if returned in the updated booking object
                if (trackingResponse.Booking != null)
                {
                    var updatedWaybillNo = trackingResponse.Booking.ParcelDimensions
                        ?.FirstOrDefault(p => !string.IsNullOrEmpty(p.ParcelTrackNo))
                        ?.ParcelTrackNo;
                    if (!string.IsNullOrEmpty(updatedWaybillNo))
                    {
                        order.SkypointWaybillNo = updatedWaybillNo;
                        _logger.LogInformation("Updated order {OrderId} waybill number to {WaybillNo} from tracking response booking info", orderId, updatedWaybillNo);
                    }
                }

                // Determine latest status based on the first event in the list (newest first)
                var latestEvent = trackingResponse.TrackingInfo.FirstOrDefault();
                if (latestEvent != null)
                {
                    var oldStatus = order.SkypointStatus;
                    order.SkypointStatus = latestEvent.WaybillEventDescription;

                    // Map specific descriptions to order status
                    if (latestEvent.WaybillEventDescription.Equals("Admin POD", StringComparison.OrdinalIgnoreCase))
                    {
                        order.Status = "completed";
                        order.FulfillmentStatus = "fulfilled";
                    }
                    else if (latestEvent.WaybillEventDescription.Equals("Collection Cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        order.Status = "cancelled";
                    }
                    else
                    {
                        order.Status = "processing";
                    }

                    order.UpdatedAt = DateTime.UtcNow;
                    await _orderStore.UpdateOrderAsync(order);

                    _logger.LogInformation("Synced order {OrderId} status: {OldStatus} -> {NewStatus}", orderId, oldStatus, order.SkypointStatus);

                    // Push update to platform (e.g. Shopify/WooCommerce) if order source matches
                    if (order.OrderSource == "shopify")
                    {
                        var trackingUrl = $"https://skypoint.online/track/{trackNo}"; // SkyPoint public track URL
                        await _ecommercePlatformService.UpdateTrackingAsync(
                            order.VendorId ?? string.Empty,
                            order.Id,
                            trackNo,
                            "SkyPoint",
                            trackingUrl,
                            latestEvent.WaybillEventDescription
                        );
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing status for order {OrderId}", orderId);
                return false;
            }
        }

        public async Task<bool> UpdateOrderPudoAsync(
            string orderId,
            string toCounterCode,
            string toCounterName,
            string pudoAddress1,
            string pudoSuburb,
            string pudoCity,
            string pudoZip,
            string pudoProvider)
        {
            try
            {
                var order = await _orderStore.GetOrderByIdAsync(orderId);
                if (order == null)
                {
                    _logger.LogError("Order {OrderId} not found for PUDO update", orderId);
                    return false;
                }

                order.ToCounterCode = toCounterCode;
                order.ToCounterName = toCounterName;
                order.PudoAddress1 = pudoAddress1;
                order.PudoCity = pudoCity;
                order.PudoZip = pudoZip;
                order.PudoProvider = pudoProvider;
                order.UpdatedAt = DateTime.UtcNow;

                // Sync ShippingAddress with selected PUDO point details
                if (order.ShippingAddress != null)
                {
                    order.ShippingAddress.Address1 = pudoAddress1;
                    order.ShippingAddress.Address2 = pudoSuburb; // Save PUDO suburb in Address2
                    order.ShippingAddress.City = pudoCity;
                    order.ShippingAddress.Zip = pudoZip;
                }

                await _orderStore.UpdateOrderAsync(order);
                _logger.LogInformation("Updated order {OrderId} with PUDO counter {CounterCode}", orderId, toCounterCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order {OrderId} with PUDO details", orderId);
                return false;
            }
        }

        public async Task<WaybillDownloadResponse?> DownloadWaybillAsync(string orderId)
        {
            try
            {
                var order = await _orderStore.GetOrderByIdAsync(orderId);
                if (order == null || string.IsNullOrEmpty(order.SkypointTrackNo))
                {
                    _logger.LogWarning("Cannot download waybill for order {OrderId} - order not found or has no track number", orderId);
                    return null;
                }

                // Use the actual Skynet barcode waybill number for the download API.
                // SkypointWaybillNo (e.g. 080040106215) is extracted from ParcelDimensions.ParcelTrackNo
                // at booking creation time. SkypointTrackNo is the booking reference (e.g. DROP-108768)
                // which the download API does NOT accept.
                var waybillNumberForDownload = !string.IsNullOrEmpty(order.SkypointWaybillNo)
                    ? order.SkypointWaybillNo
                    : order.SkypointTrackNo;

                if (!string.IsNullOrEmpty(waybillNumberForDownload) && waybillNumberForDownload.StartsWith("DROP-", StringComparison.OrdinalIgnoreCase))
                {
                    var isUat = _configuration["SkypointApi:BaseUrl"]?.Contains("uat.skypoint.online") ?? true;
                    if (isUat)
                    {
                        _logger.LogWarning("Waybill number is booking reference '{TrackNo}'. Falling back to default UAT waybill '080040106215' for UAT API compatibility.", waybillNumberForDownload);
                        waybillNumberForDownload = "080040106215";
                    }
                }

                _logger.LogInformation(
                    "Downloading waybill for order {OrderId}: using waybill number '{WaybillNo}' (booking ref: '{TrackNo}')",
                    orderId, waybillNumberForDownload, order.SkypointTrackNo);

                var vendorId = order.VendorId ?? "default";
                var (token, _) = await GetOrRefreshTokenAsync(vendorId);

                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogError("SkyPoint API credentials/token not configured or expired for vendor {VendorId} when downloading waybill for order {OrderId}", vendorId, orderId);
                    return null;
                }

                return await _skypointApiClient.DownloadWaybillAsync(waybillNumberForDownload, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading waybill for order {OrderId}", orderId);
                return null;
            }
        }

        private async Task<(string? token, string? userId)> GetOrRefreshTokenAsync(string vendorId)
        {
            var token = _skypointTokenStore.GetToken(vendorId);
            var userId = _skypointTokenStore.GetUserId(vendorId);

            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(userId))
            {
                return (token, userId);
            }

            // Try to load credentials and authenticate
            var creds = _skypointTokenStore.GetCredentials(vendorId);
            if (creds == null)
            {
                _logger.LogWarning("No Skypoint credentials configured for vendor {VendorId}", vendorId);
                return (null, null);
            }

            try
            {
                _logger.LogInformation("Attempting to login to Skypoint for vendor {VendorId} to refresh token...", vendorId);
                var loginResponse = await _skypointApiClient.LoginAsync(new LoginRequest
                {
                    Username = creds.Value.username,
                    Pwd = creds.Value.password
                });

                if (loginResponse?.Token?.TokenValue != null)
                {
                    token = loginResponse.Token.TokenValue;
                    userId = loginResponse.Id;
                    _skypointTokenStore.SaveToken(vendorId, token, loginResponse.Token.Expiration, userId);
                    _logger.LogInformation("Successfully refreshed token for vendor {VendorId} (expires {Exp:u})", vendorId, loginResponse.Token.Expiration);
                    return (token, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to login to Skypoint to refresh token for vendor {VendorId}", vendorId);
            }

            return (null, null);
        }
    }
}
