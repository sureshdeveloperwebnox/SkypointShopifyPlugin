using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    /// <summary>
    /// Interface for storing and retrieving Skypoint orders
    /// Mirrors the pattern used for token stores
    /// </summary>
    public interface ISkypointOrderStore
    {
        /// <summary>
        /// Save a new order to storage
        /// </summary>
        Task SaveOrderAsync(SkypointOrder order);

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
        Task<List<SkypointOrder>> GetOrdersAsync(SkypointOrderFilter filter);

        /// <summary>
        /// Update an existing order
        /// </summary>
        Task UpdateOrderAsync(SkypointOrder order);

        /// <summary>
        /// Delete an order
        /// </summary>
        Task DeleteOrderAsync(string orderId);

        /// <summary>
        /// Get total count of orders matching filter
        /// </summary>
        Task<int> GetOrderCountAsync(SkypointOrderFilter filter);

        /// <summary>
        /// Check if an order has been processed (has Skypoint booking ID)
        /// </summary>
        Task<bool> IsOrderProcessedAsync(string orderId);

        /// <summary>
        /// Mark an order as processed with Skypoint booking details
        /// </summary>
        Task MarkOrderAsProcessedAsync(string orderId, string bookingId, string trackNo, string status);
    }
}
