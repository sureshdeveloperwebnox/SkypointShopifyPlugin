using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

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

        private static readonly Dictionary<char, string> Code39Patterns = new()
        {
            {'0', "n n n w w n w n n"},
            {'1', "w n n w n n n n w"},
            {'2', "n n w w n n n n w"},
            {'3', "w n w w n n n n n"},
            {'4', "n n n w w n n n w"},
            {'5', "w n n w w n n n n"},
            {'6', "n n w w w n n n n"},
            {'7', "n n n w n n w n w"},
            {'8', "w n n w n n w n n"},
            {'9', "n n w w n n w n n"},
            {'A', "w n n n n w n n w"},
            {'B', "n n w n n w n n w"},
            {'C', "w n w n n w n n n"},
            {'D', "n n n n w w n n w"},
            {'E', "w n n n w w n n n"},
            {'F', "n n w n w w n n n"},
            {'G', "n n n n n w w n w"},
            {'H', "w n n n n w w n n"},
            {'I', "n n w n n w w n n"},
            {'J', "n n n n w w w n n"},
            {'K', "w n n n n n n w w"},
            {'L', "n n w n n n n w w"},
            {'M', "w n w n n n n w n"},
            {'N', "n n n n w n n w w"},
            {'O', "w n n n w n n w n"},
            {'P', "n n w n w n n w n"},
            {'Q', "n n n n n n w w w"},
            {'R', "w n n n n n w w n"},
            {'S', "n n w n n n w w n"},
            {'T', "n n n n w n w w n"},
            {'U', "w w n n n n n n w"},
            {'V', "n w w n n n n n w"},
            {'W', "w w w n n n n n n"},
            {'X', "n w n n w n n n w"},
            {'Y', "w w n n w n n n n"},
            {'Z', "n w w n w n n n n"},
            {'-', "n w n n n n w n w"},
            {'.', "w w n n n n w n n"},
            {' ', "n w w n n n w n n"},
            {'*', "n w n n w n w n n"},
            {'$', "n w n w n w n n n"},
            {'/', "n w n w n n n w n"},
            {'+', "n w n n n w n w n"},
            {'%', "n n n w n w n w n"}
        };

        private static double GetCode39Width(string text, double narrowWidth, double wideWidth, double gapWidth)
        {
            double totalWidth = 0;
            foreach (var ch in text)
            {
                if (!Code39Patterns.TryGetValue(ch, out var pattern))
                    continue;

                var elements = pattern.Split(' ');
                foreach (var elem in elements)
                {
                    totalWidth += (elem == "w") ? wideWidth : narrowWidth;
                }
                totalWidth += gapWidth;
            }
            return totalWidth - gapWidth;
        }

        private static void DrawCode39Barcode(XGraphics gfx, double y, double height, string text, double pageWidth)
        {
            text = text.ToUpper();
            if (!text.StartsWith("*")) text = "*" + text;
            if (!text.EndsWith("*")) text = text + "*";

            double narrowWidth = 0.8;
            double wideWidth = 2.0;
            double gapWidth = 0.8;

            double totalWidth = GetCode39Width(text, narrowWidth, wideWidth, gapWidth);
            double currentX = (pageWidth - totalWidth) / 2;

            XBrush blackBrush = XBrushes.Black;

            foreach (var ch in text)
            {
                if (!Code39Patterns.TryGetValue(ch, out var pattern))
                    continue;

                var elements = pattern.Split(' ');
                bool isBar = true;

                foreach (var elem in elements)
                {
                    double elemWidth = (elem == "w") ? wideWidth : narrowWidth;
                    if (isBar)
                    {
                        gfx.DrawRectangle(blackBrush, currentX, y, elemWidth, height);
                    }
                    currentX += elemWidth;
                    isBar = !isBar;
                }

                currentX += gapWidth;
            }
        }

        private WaybillDownloadResponse GenerateWaybillPdfLocally(SkypointOrder order)
        {
            EnsureFontResolver();
            var document = new PdfDocument();
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(100);
            page.Height = XUnit.FromMillimeter(150);

            var gfx = XGraphics.FromPdfPage(page);
            var margin = 10.0;
            var borderPen = new XPen(XColors.Black, 1.5);
            gfx.DrawRectangle(borderPen, margin, margin, page.Width - 2 * margin, page.Height - 2 * margin);

            // Title
            var titleFont = new XFont("Helvetica-Bold", 12);
            var subTitleFont = new XFont("Helvetica", 7);
            gfx.DrawString("SKYNET WORLDWIDE EXPRESS", titleFont, XBrushes.Black, new XRect(margin, margin + 5, page.Width - 2 * margin, 15), XStringFormats.TopCenter);
            gfx.DrawString("SHOPIFY API PLUGIN DELIVERIES (SANDBOX)", subTitleFont, XBrushes.Black, new XRect(margin, margin + 18, page.Width - 2 * margin, 10), XStringFormats.TopCenter);

            // Divider
            var dividerPen = new XPen(XColors.Black, 0.5);
            var y = margin + 30.0;
            gfx.DrawLine(dividerPen, margin, y, page.Width - margin, y);

            // Service details
            y += 5;
            var labelFont = new XFont("Helvetica-Bold", 8);
            var valueFont = new XFont("Helvetica", 8);
            var orderNoFont = new XFont("Helvetica-Bold", 10);
            
            gfx.DrawString("ORDER NO:", labelFont, XBrushes.Black, margin + 5, y + 10);
            gfx.DrawString($"#{order.OrderNumber}", orderNoFont, XBrushes.Black, margin + 55, y + 10);

            var serviceType = !string.IsNullOrEmpty(order.ToCounterCode) ? "PUDO / TO COUNTER" : "ROAD / DOOR TO DOOR";
            gfx.DrawString("SERVICE:", labelFont, XBrushes.Black, margin + 5, y + 22);
            gfx.DrawString(serviceType, labelFont, XBrushes.Black, margin + 55, y + 22);

            y += 30;
            gfx.DrawLine(dividerPen, margin, y, page.Width - margin, y);

            // FROM / Sender Info
            y += 5;
            gfx.DrawString("FROM (SENDER):", labelFont, XBrushes.Black, margin + 5, y + 8);
            var senderName = order.BillingAddress != null ? $"{order.BillingAddress.FirstName} {order.BillingAddress.LastName}" : "Shopify Vendor";
            var senderPhone = order.BillingAddress?.Phone ?? " ";
            var senderAddress = order.BillingAddress != null 
                ? $"{order.BillingAddress.Address1}, {order.BillingAddress.City}, {order.BillingAddress.Zip}"
                : "Default Warehouse Address";

            gfx.DrawString(senderName, valueFont, XBrushes.Black, margin + 5, y + 18);
            gfx.DrawString(senderAddress, valueFont, XBrushes.Black, margin + 5, y + 28);
            gfx.DrawString($"TEL: {senderPhone}", valueFont, XBrushes.Black, margin + 5, y + 38);

            y += 45;
            gfx.DrawLine(dividerPen, margin, y, page.Width - margin, y);

            // TO / Recipient Info
            y += 5;
            gfx.DrawString("TO (RECIPIENT):", labelFont, XBrushes.Black, margin + 5, y + 8);
            var recipientName = order.ShippingAddress != null ? $"{order.ShippingAddress.FirstName} {order.ShippingAddress.LastName}" : $"{order.Customer?.FirstName} {order.Customer?.LastName}";
            var recipientPhone = order.ShippingAddress?.Phone ?? order.Customer?.Phone ?? " ";
            var recipientEmail = order.Customer?.Email ?? " ";
            var recipientAddress = order.ShippingAddress != null 
                ? $"{order.ShippingAddress.Address1}, {order.ShippingAddress.City}, {order.ShippingAddress.Zip}"
                : " ";

            gfx.DrawString(recipientName, valueFont, XBrushes.Black, margin + 5, y + 18);
            gfx.DrawString(recipientAddress, valueFont, XBrushes.Black, margin + 5, y + 28);
            gfx.DrawString($"TEL: {recipientPhone} | EMAIL: {recipientEmail}", valueFont, XBrushes.Black, margin + 5, y + 38);

            // DTC PUDO Details
            if (!string.IsNullOrEmpty(order.ToCounterCode))
            {
                y += 45;
                // Draw light gray box for PUDO Counter
                var pudoBoxBrush = new XSolidBrush(XColor.FromArgb(240, 240, 240));
                gfx.DrawRectangle(pudoBoxBrush, margin + 3, y, page.Width - 2 * margin - 6, 32);
                gfx.DrawRectangle(new XPen(XColors.LightGray, 0.5), margin + 3, y, page.Width - 2 * margin - 6, 32);

                gfx.DrawString($"TO COUNTER CODE: {order.ToCounterCode}", labelFont, XBrushes.Black, margin + 8, y + 10);
                gfx.DrawString($"COUNTER NAME: {order.ToCounterName}", valueFont, XBrushes.Black, margin + 8, y + 22);
                y += 35;
            }
            else
            {
                y += 45;
            }

            gfx.DrawLine(dividerPen, margin, y, page.Width - margin, y);

            // Barcode section
            y += 10;
            var barcodeVal = order.SkypointTrackNo ?? $"DROP-{order.OrderNumber}";
            DrawCode39Barcode(gfx, y, 40, barcodeVal, page.Width);
            
            y += 45;
            var barcodeFont = new XFont("Helvetica-Bold", 10);
            gfx.DrawString(barcodeVal, barcodeFont, XBrushes.Black, new XRect(margin, y, page.Width - 2 * margin, 15), XStringFormats.TopCenter);

            using var ms = new MemoryStream();
            document.Save(ms);
            var bytes = ms.ToArray();

            return new WaybillDownloadResponse
            {
                FileName = $"waybill_{order.OrderNumber}.pdf",
                ApplicationType = "application/pdf",
                FileStream = Convert.ToBase64String(bytes)
            };
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

                // If we are in UAT sandbox mode and the order does not have an assigned barcode waybill number yet
                // (e.g. still DROP-xxxxx because it's unpaid), we dynamically generate a high-quality local label PDF 
                // containing the actual order info, address, and a scannable Code 39 barcode.
                var isUat = _configuration["SkypointApi:BaseUrl"]?.Contains("uat.skypoint.online") ?? true;
                var waybillNumberForDownload = !string.IsNullOrEmpty(order.SkypointWaybillNo)
                    ? order.SkypointWaybillNo
                    : order.SkypointTrackNo;

                if (isUat && (string.IsNullOrEmpty(waybillNumberForDownload) || waybillNumberForDownload.StartsWith("DROP-", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("UAT Sandbox Mode: Generating dynamic local shipping label PDF with barcode for order {OrderNumber} (booking ref: {TrackNo})", order.OrderNumber, order.SkypointTrackNo);
                    return GenerateWaybillPdfLocally(order);
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

                var response = await _skypointApiClient.DownloadWaybillAsync(waybillNumberForDownload, token);
                if (response != null)
                {
                    response.FileName = $"waybill_{order.OrderNumber}.pdf";
                }
                return response;
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

        private static bool _fontResolverRegistered = false;
        private static readonly object _fontResolverLock = new();

        private static void EnsureFontResolver()
        {
            if (!_fontResolverRegistered)
            {
                lock (_fontResolverLock)
                {
                    if (!_fontResolverRegistered)
                    {
                        try
                        {
                            GlobalFontSettings.FontResolver = new SimpleFontResolver();
                        }
                        catch (Exception)
                        {
                            // Already registered
                        }
                        _fontResolverRegistered = true;
                    }
                }
            }
        }
    }

    public class SimpleFontResolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string fontFileName = "arial.ttf";
            if (isBold && isItalic) fontFileName = "arialbi.ttf";
            else if (isBold) fontFileName = "arialbd.ttf";
            else if (isItalic) fontFileName = "ariali.ttf";

            return new FontResolverInfo(fontFileName);
        }

        public byte[]? GetFont(string faceName)
        {
            // Windows standard fonts path
            var windowsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", faceName);
            if (File.Exists(windowsPath))
                return File.ReadAllBytes(windowsPath);

            // Linux standard search paths
            var searchPaths = new[]
            {
                "/usr/share/fonts/truetype/msttcorefonts",
                "/usr/share/fonts/truetype/dejavu",
                "/usr/share/fonts/dejavu",
                "/usr/share/fonts"
            };

            foreach (var dir in searchPaths)
            {
                if (Directory.Exists(dir))
                {
                    var file = Path.Combine(dir, faceName);
                    if (File.Exists(file))
                        return File.ReadAllBytes(file);

                    // Case-insensitive search on Linux
                    try
                    {
                        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            if (Path.GetFileName(f).Equals(faceName, StringComparison.OrdinalIgnoreCase))
                                return File.ReadAllBytes(f);
                        }
                    }
                    catch { }
                }
            }

            return null;
        }
    }
}
