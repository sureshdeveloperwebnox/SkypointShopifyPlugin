using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    /// <summary>
    /// Service interface for Skypoint order management
    /// Handles order creation, processing, and lifecycle management
    /// </summary>
    public interface ISkypointOrderService
    {
        /// <summary>
        /// Create a new Skypoint order and optionally process it into a booking
        /// </summary>
        Task<SkypointOrderResponse> CreateOrderAsync(CreateSkypointOrderRequest request, bool autoProcess = true);

        /// <summary>
        /// Process an existing order into a Skypoint booking
        /// </summary>
        Task<SkypointOrderResponse> ProcessOrderAsync(string orderId);

        /// <summary>
        /// Get an order by ID
        /// </summary>
        Task<SkypointOrder?> GetOrderByIdAsync(string orderId);

        /// <summary>
        /// Get an order by order number
        /// </summary>
        Task<SkypointOrder?> GetOrderByNumberAsync(string orderNumber);

        /// <summary>
        /// Get orders with filtering and pagination
        /// </summary>
        Task<SkypointOrderListResponse> GetOrdersAsync(SkypointOrderFilter filter);

        /// <summary>
        /// Update order status
        /// </summary>
        Task<bool> UpdateOrderStatusAsync(string orderId, string status);

        /// <summary>
        /// Update order with Skypoint booking details
        /// </summary>
        Task<bool> UpdateOrderWithBookingAsync(string orderId, string bookingId, string trackNo, string status);

        /// <summary>
        /// Delete an order
        /// </summary>
        Task<bool> DeleteOrderAsync(string orderId);

        /// <summary>
        /// Sync order status from Skypoint API
        /// </summary>
        Task<bool> SyncOrderStatusAsync(string orderId);

        /// <summary>
        /// Update order with PUDO counter details
        /// </summary>
        Task<bool> UpdateOrderPudoAsync(
            string orderId,
            string toCounterCode,
            string toCounterName,
            string pudoAddress1,
            string pudoCity,
            string pudoZip,
            string pudoProvider);
    }
}
