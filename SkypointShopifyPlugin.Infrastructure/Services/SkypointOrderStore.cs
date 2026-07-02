using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Infrastructure.Data;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class SkypointOrderStoreOptions
    {
        public const string SectionName = "SkypointOrderStore";
        public string DataDirectory { get; set; } = "data/orders";
    }

    /// <summary>
    /// Database-backed store for Skypoint orders.
    /// Provides stateless order persistence and highly efficient querying using LINQ.
    /// Includes a one-time migration for legacy file-based order JSONs.
    /// </summary>
    public class SkypointOrderStore : ISkypointOrderStore
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SkypointOrderStore> _logger;
        private readonly string _ordersDirectory;

        public SkypointOrderStore(
            IServiceScopeFactory scopeFactory,
            ILogger<SkypointOrderStore> logger,
            IOptions<SkypointOrderStoreOptions> options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _ordersDirectory = options.Value.DataDirectory;

            // Perform legacy orders migration on startup
            MigrateLegacyOrders();
        }

        public async Task SaveOrderAsync(SkypointOrder order)
        {
            var entity = MapToEntity(order);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var existing = await db.SkypointOrders.FindAsync(order.Id);
            if (existing == null)
            {
                db.SkypointOrders.Add(entity);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(entity);
                // Handle complex fields which require explicit updates since they are not mapped simple values
                existing.CustomerJson = entity.CustomerJson;
                existing.LineItemsJson = entity.LineItemsJson;
                existing.ShippingAddressJson = entity.ShippingAddressJson;
                existing.BillingAddressJson = entity.BillingAddressJson;
                db.SkypointOrders.Update(existing);
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Saved order {OrderId} to database", order.Id);
        }

        public async Task<SkypointOrder?> GetOrderByIdAsync(string orderId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = await db.SkypointOrders.FindAsync(orderId);
            if (entity == null)
            {
                return null;
            }

            return MapToDto(entity);
        }

        public async Task<SkypointOrder?> GetOrderByNumberAsync(string orderNumber)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = db.SkypointOrders.FirstOrDefault(o => o.OrderNumber == orderNumber);
            if (entity == null)
            {
                return null;
            }

            return MapToDto(entity);
        }

        public async Task<List<SkypointOrder>> GetOrdersAsync(SkypointOrderFilter filter)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var query = db.SkypointOrders.AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(filter.VendorId))
            {
                query = query.Where(o => o.VendorId == filter.VendorId);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                query = query.Where(o => o.Status == filter.Status);
            }

            if (!string.IsNullOrEmpty(filter.OrderSource))
            {
                query = query.Where(o => o.OrderSource == filter.OrderSource);
            }

            if (filter.StartDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt >= filter.StartDate.Value);
            }

            if (filter.EndDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt <= filter.EndDate.Value);
            }

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var search = filter.SearchTerm.ToLower();
                query = query.Where(o =>
                    o.OrderNumber.ToLower().Contains(search) ||
                    (o.CustomerJson != null && o.CustomerJson.ToLower().Contains(search)));
            }

            // Apply sorting
            query = filter.SortBy.ToLowerInvariant() switch
            {
                "created_at" => filter.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? query.OrderBy(o => o.CreatedAt)
                    : query.OrderByDescending(o => o.CreatedAt),
                "updated_at" => filter.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? query.OrderBy(o => o.UpdatedAt)
                    : query.OrderByDescending(o => o.UpdatedAt),
                "total_price" => filter.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? query.OrderBy(o => o.TotalPrice)
                    : query.OrderByDescending(o => o.TotalPrice),
                "order_number" => filter.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? query.OrderBy(o => o.OrderNumber)
                    : query.OrderByDescending(o => o.OrderNumber),
                _ => query.OrderByDescending(o => o.CreatedAt)
            };

            // Apply pagination
            var list = query
                .Skip((filter.Page - 1) * filter.Limit)
                .Take(filter.Limit)
                .ToList();

            return list.Select(MapToDto).ToList();
        }

        public async Task UpdateOrderAsync(SkypointOrder order)
        {
            order.UpdatedAt = DateTime.UtcNow;
            await SaveOrderAsync(order);
        }

        public async Task DeleteOrderAsync(string orderId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = await db.SkypointOrders.FindAsync(orderId);
            if (entity != null)
            {
                db.SkypointOrders.Remove(entity);
                await db.SaveChangesAsync();
                _logger.LogInformation("Deleted order {OrderId} from database", orderId);
            }
        }

        public async Task<int> GetOrderCountAsync(SkypointOrderFilter filter)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var query = db.SkypointOrders.AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(filter.VendorId))
            {
                query = query.Where(o => o.VendorId == filter.VendorId);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                query = query.Where(o => o.Status == filter.Status);
            }

            if (!string.IsNullOrEmpty(filter.OrderSource))
            {
                query = query.Where(o => o.OrderSource == filter.OrderSource);
            }

            if (filter.StartDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt >= filter.StartDate.Value);
            }

            if (filter.EndDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt <= filter.EndDate.Value);
            }

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var search = filter.SearchTerm.ToLower();
                query = query.Where(o =>
                    o.OrderNumber.ToLower().Contains(search) ||
                    (o.CustomerJson != null && o.CustomerJson.ToLower().Contains(search)));
            }

            return query.Count();
        }

        public async Task<bool> IsOrderProcessedAsync(string orderId)
        {
            var order = await GetOrderByIdAsync(orderId);
            return order != null && !string.IsNullOrEmpty(order.SkypointBookingId);
        }

        public async Task MarkOrderAsProcessedAsync(string orderId, string bookingId, string trackNo, string status)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

            var entity = await db.SkypointOrders.FindAsync(orderId);
            if (entity != null)
            {
                entity.SkypointBookingId = bookingId;
                entity.SkypointTrackNo = trackNo;
                entity.SkypointStatus = status;
                entity.Status = "processing";
                entity.UpdatedAt = DateTime.UtcNow;

                db.SkypointOrders.Update(entity);
                await db.SaveChangesAsync();
                _logger.LogInformation("Marked order {OrderId} as processed with booking {BookingId} in database", orderId, bookingId);
            }
        }

        private static SkypointOrderEntity MapToEntity(SkypointOrder order)
        {
            return new SkypointOrderEntity
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                FinancialStatus = order.FinancialStatus,
                FulfillmentStatus = order.FulfillmentStatus,
                TotalPrice = order.TotalPrice,
                Currency = order.Currency,
                Status = order.Status,
                SkypointBookingId = order.SkypointBookingId,
                SkypointTrackNo = order.SkypointTrackNo,
                SkypointStatus = order.SkypointStatus,
                OrderSource = order.OrderSource,
                VendorId = order.VendorId,
                CustomerJson = order.Customer != null ? JsonSerializer.Serialize(order.Customer) : null,
                LineItemsJson = order.LineItems != null ? JsonSerializer.Serialize(order.LineItems) : null,
                ShippingAddressJson = order.ShippingAddress != null ? JsonSerializer.Serialize(order.ShippingAddress) : null,
                BillingAddressJson = order.BillingAddress != null ? JsonSerializer.Serialize(order.BillingAddress) : null
            };
        }

        private static SkypointOrder MapToDto(SkypointOrderEntity entity)
        {
            return new SkypointOrder
            {
                Id = entity.Id,
                OrderNumber = entity.OrderNumber,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                FinancialStatus = entity.FinancialStatus,
                FulfillmentStatus = entity.FulfillmentStatus,
                TotalPrice = entity.TotalPrice,
                Currency = entity.Currency,
                Status = entity.Status,
                SkypointBookingId = entity.SkypointBookingId,
                SkypointTrackNo = entity.SkypointTrackNo,
                SkypointStatus = entity.SkypointStatus,
                OrderSource = entity.OrderSource,
                VendorId = entity.VendorId,
                Customer = !string.IsNullOrEmpty(entity.CustomerJson) ? JsonSerializer.Deserialize<SkypointCustomer>(entity.CustomerJson) : null,
                LineItems = !string.IsNullOrEmpty(entity.LineItemsJson) ? JsonSerializer.Deserialize<List<SkypointOrderItem>>(entity.LineItemsJson) ?? new() : new(),
                ShippingAddress = !string.IsNullOrEmpty(entity.ShippingAddressJson) ? JsonSerializer.Deserialize<SkypointAddress>(entity.ShippingAddressJson) : null,
                BillingAddress = !string.IsNullOrEmpty(entity.BillingAddressJson) ? JsonSerializer.Deserialize<SkypointAddress>(entity.BillingAddressJson) : null
            };
        }

        private void MigrateLegacyOrders()
        {
            try
            {
                if (Directory.Exists(_ordersDirectory))
                {
                    var files = Directory.GetFiles(_ordersDirectory, "*.json");
                    if (files.Length == 0) return;

                    _logger.LogInformation("Found {Count} legacy order JSON files. Starting migration.", files.Length);

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<SkypointDbContext>();

                    var migratedCount = 0;
                    foreach (var file in files)
                    {
                        try
                        {
                            var json = File.ReadAllText(file);
                            var order = JsonSerializer.Deserialize<SkypointOrder>(json);
                            if (order != null)
                            {
                                if (!db.SkypointOrders.Any(o => o.Id == order.Id))
                                {
                                    db.SkypointOrders.Add(MapToEntity(order));
                                    migratedCount++;
                                }
                            }
                            
                            // Move/rename file so it doesn't get processed again
                            File.Move(file, file + ".migrated", overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to migrate order file: {File}", file);
                        }
                    }

                    if (migratedCount > 0)
                    {
                        db.SaveChanges();
                        _logger.LogInformation("Successfully migrated {Count} orders to the database.", migratedCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during legacy order migration.");
            }
        }
    }
}
