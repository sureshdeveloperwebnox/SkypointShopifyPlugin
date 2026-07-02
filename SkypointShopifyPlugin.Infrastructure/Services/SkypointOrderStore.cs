using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class SkypointOrderStoreOptions
    {
        public const string SectionName = "SkypointOrderStore";
        public string DataDirectory { get; set; } = "data/orders";
    }

    /// <summary>
    /// File-based storage for Skypoint orders.
    /// Stores orders as JSON files in the data directory.
    /// Includes thread-safe synchronization, LINQ queries, and startup restoration of previously migrated orders.
    /// </summary>
    public class SkypointOrderStore : ISkypointOrderStore
    {
        private readonly ILogger<SkypointOrderStore> _logger;
        private readonly string _ordersDirectory;
        private readonly object _lock = new();

        public SkypointOrderStore(
            ILogger<SkypointOrderStore> logger,
            IOptions<SkypointOrderStoreOptions> options)
        {
            _logger = logger;
            _ordersDirectory = options.Value.DataDirectory;
            
            // Ensure directory exists
            if (!Directory.Exists(_ordersDirectory))
            {
                Directory.CreateDirectory(_ordersDirectory);
                _logger.LogInformation("Created orders directory: {Directory}", _ordersDirectory);
            }
            else
            {
                // Restore migrated orders if any
                RestoreMigratedOrders();
            }
        }

        public async Task SaveOrderAsync(SkypointOrder order)
        {
            lock (_lock)
            {
                var filePath = GetOrderFilePath(order.Id);
                var json = JsonSerializer.Serialize(order, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(filePath, json);
                _logger.LogInformation("Saved order {OrderId} to {FilePath}", order.Id, filePath);
            }
            await Task.CompletedTask;
        }

        public async Task<SkypointOrder?> GetOrderByIdAsync(string orderId)
        {
            var filePath = GetOrderFilePath(orderId);
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("Order not found: {OrderId}", orderId);
                return null;
            }

            lock (_lock)
            {
                var json = File.ReadAllText(filePath);
                var order = JsonSerializer.Deserialize<SkypointOrder>(json);
                return order;
            }
        }

        public async Task<SkypointOrder?> GetOrderByNumberAsync(string orderNumber)
        {
            var orders = await GetAllOrdersAsync();
            return orders.FirstOrDefault(o => o.OrderNumber.Equals(orderNumber, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<SkypointOrder>> GetOrdersAsync(SkypointOrderFilter filter)
        {
            var allOrders = await GetAllOrdersAsync();
            
            // Apply filters
            var filtered = allOrders.AsEnumerable();

            if (!string.IsNullOrEmpty(filter.VendorId))
            {
                filtered = filtered.Where(o => o.VendorId == filter.VendorId);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                filtered = filtered.Where(o => o.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(filter.OrderSource))
            {
                filtered = filtered.Where(o => o.OrderSource.Equals(filter.OrderSource, StringComparison.OrdinalIgnoreCase));
            }

            if (filter.StartDate.HasValue)
            {
                filtered = filtered.Where(o => o.CreatedAt >= filter.StartDate.Value);
            }

            if (filter.EndDate.HasValue)
            {
                filtered = filtered.Where(o => o.CreatedAt <= filter.EndDate.Value);
            }

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var searchLower = filter.SearchTerm.ToLowerInvariant();
                filtered = filtered.Where(o =>
                    o.OrderNumber.ToLowerInvariant().Contains(searchLower) ||
                    (o.Customer?.Email?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (o.Customer?.FirstName?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (o.Customer?.LastName?.ToLowerInvariant().Contains(searchLower) ?? false));
            }

            // Apply sorting
            filtered = filter.SortBy.ToLowerInvariant() switch
            {
                "created_at" => filter.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? filtered.OrderBy(o => o.CreatedAt)
                    : filtered.OrderByDescending(o => o.CreatedAt),
                "updated_at" => filter.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? filtered.OrderBy(o => o.UpdatedAt)
                    : filtered.OrderByDescending(o => o.UpdatedAt),
                "total_price" => filter.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? filtered.OrderBy(o => o.TotalPrice)
                    : filtered.OrderByDescending(o => o.TotalPrice),
                "order_number" => filter.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? filtered.OrderBy(o => o.OrderNumber)
                    : filtered.OrderByDescending(o => o.OrderNumber),
                _ => filtered.OrderByDescending(o => o.CreatedAt)
            };

            // Apply pagination
            var paginated = filtered
                .Skip((filter.Page - 1) * filter.Limit)
                .Take(filter.Limit)
                .ToList();

            return paginated;
        }

        public async Task UpdateOrderAsync(SkypointOrder order)
        {
            order.UpdatedAt = DateTime.UtcNow;
            await SaveOrderAsync(order);
        }

        public async Task DeleteOrderAsync(string orderId)
        {
            var filePath = GetOrderFilePath(orderId);
            if (File.Exists(filePath))
            {
                lock (_lock)
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Deleted order {OrderId}", orderId);
                }
            }
            await Task.CompletedTask;
        }

        public async Task<int> GetOrderCountAsync(SkypointOrderFilter filter)
        {
            var allOrders = await GetAllOrdersAsync();
            
            // Apply same filters as GetOrdersAsync
            var filtered = allOrders.AsEnumerable();

            if (!string.IsNullOrEmpty(filter.VendorId))
            {
                filtered = filtered.Where(o => o.VendorId == filter.VendorId);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                filtered = filtered.Where(o => o.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(filter.OrderSource))
            {
                filtered = filtered.Where(o => o.OrderSource.Equals(filter.OrderSource, StringComparison.OrdinalIgnoreCase));
            }

            if (filter.StartDate.HasValue)
            {
                filtered = filtered.Where(o => o.CreatedAt >= filter.StartDate.Value);
            }

            if (filter.EndDate.HasValue)
            {
                filtered = filtered.Where(o => o.CreatedAt <= filter.EndDate.Value);
            }

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var searchLower = filter.SearchTerm.ToLowerInvariant();
                filtered = filtered.Where(o =>
                    o.OrderNumber.ToLowerInvariant().Contains(searchLower) ||
                    (o.Customer?.Email?.ToLowerInvariant().Contains(searchLower) ?? false));
            }

            return filtered.Count();
        }

        public async Task<bool> IsOrderProcessedAsync(string orderId)
        {
            var order = await GetOrderByIdAsync(orderId);
            return order != null && !string.IsNullOrEmpty(order.SkypointBookingId);
        }

        public async Task MarkOrderAsProcessedAsync(string orderId, string bookingId, string trackNo, string status)
        {
            var order = await GetOrderByIdAsync(orderId);
            if (order != null)
            {
                order.SkypointBookingId = bookingId;
                order.SkypointTrackNo = trackNo;
                order.SkypointStatus = status;
                order.Status = "processing";
                order.UpdatedAt = DateTime.UtcNow;
                await SaveOrderAsync(order);
                _logger.LogInformation("Marked order {OrderId} as processed with booking {BookingId}", orderId, bookingId);
            }
        }

        private List<SkypointOrder> GetAllOrders()
        {
            lock (_lock)
            {
                var orders = new List<SkypointOrder>();
                
                if (!Directory.Exists(_ordersDirectory))
                {
                    return orders;
                }

                var files = Directory.GetFiles(_ordersDirectory, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var order = JsonSerializer.Deserialize<SkypointOrder>(json);
                        if (order != null)
                        {
                            orders.Add(order);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading order file: {File}", file);
                    }
                }

                return orders;
            }
        }

        private async Task<List<SkypointOrder>> GetAllOrdersAsync()
        {
            return await Task.FromResult(GetAllOrders());
        }

        private string GetOrderFilePath(string orderId)
        {
            var safeOrderId = orderId.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return Path.Combine(_ordersDirectory, $"{safeOrderId}.json");
        }

        private void RestoreMigratedOrders()
        {
            try
            {
                var files = Directory.GetFiles(_ordersDirectory, "*.json.migrated");
                if (files.Length == 0) return;

                _logger.LogInformation("Restoring {Count} migrated order files...", files.Length);
                var restoredCount = 0;
                foreach (var file in files)
                {
                    try
                    {
                        var original = file.Substring(0, file.Length - ".migrated".Length);
                        if (!File.Exists(original))
                        {
                            File.Move(file, original);
                            restoredCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to restore migrated order file: {File}", file);
                    }
                }
                
                if (restoredCount > 0)
                {
                    _logger.LogInformation("Successfully restored {Count} orders from migrated backups.", restoredCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore migrated orders: {Message}", ex.Message);
            }
        }
    }
}
