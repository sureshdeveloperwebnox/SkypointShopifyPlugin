using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class SkypointTrackingSyncService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SkypointTrackingSyncService> _logger;
        private readonly int _pollingIntervalMinutes = 60; // Default 1 hour

        public SkypointTrackingSyncService(
            IServiceScopeFactory scopeFactory,
            ILogger<SkypointTrackingSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SkyPoint Tracking Synchronisation background service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting scheduled SkyPoint shipment tracking sync...");
                    await SyncAllActiveOrdersAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during scheduled tracking sync.");
                }

                await Task.Delay(TimeSpan.FromMinutes(_pollingIntervalMinutes), stoppingToken);
            }

            _logger.LogInformation("SkyPoint Tracking Synchronisation background service stopped.");
        }

        private async Task SyncAllActiveOrdersAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var orderStore = scope.ServiceProvider.GetRequiredService<ISkypointOrderStore>();
            var orderService = scope.ServiceProvider.GetRequiredService<ISkypointOrderService>();

            // Fetch orders that are currently "processing" (booked, but not fully completed/delivered/cancelled)
            var filter = new SkypointOrderFilter
            {
                Status = "processing",
                Limit = 100 // Process in batches
            };

            var listResponse = await orderService.GetOrdersAsync(filter);
            if (listResponse == null || listResponse.Orders == null || listResponse.Orders.Count == 0)
            {
                _logger.LogInformation("No active orders in processing state for tracking sync.");
                return;
            }

            _logger.LogInformation("Found {Count} active order(s) to sync tracking status.", listResponse.Orders.Count);

            foreach (var order in listResponse.Orders)
            {
                // Safety check: must have a track/waybill number
                if (string.IsNullOrEmpty(order.SkypointTrackNo) && string.IsNullOrEmpty(order.SkypointBookingId))
                {
                    continue;
                }

                try
                {
                    _logger.LogInformation("Syncing tracking events for Order {OrderId} (Waybill: {TrackNo})", order.Id, order.SkypointTrackNo);
                    await orderService.SyncOrderStatusAsync(order.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync tracking for Order {OrderId}", order.Id);
                }
            }
        }
    }
}
