using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using SkypointShopifyPlugin.Core.Common;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Background Hosted Service that consumes queued webhook tasks sequentially,
    /// ensuring FIFO execution and preventing rate/ordering collisions.
    /// </summary>
    public class WebhookQueueProcessor : BackgroundService
    {
        private readonly IWebhookQueue _webhookQueue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WebhookQueueProcessor> _logger;

        public WebhookQueueProcessor(
            IWebhookQueue webhookQueue,
            IServiceScopeFactory scopeFactory,
            ILogger<WebhookQueueProcessor> logger)
        {
            _webhookQueue = webhookQueue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Webhook Queue Processor background service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var job = await _webhookQueue.DequeueWebhookAsync(stoppingToken);
                    _logger.LogInformation(LogEventIds.WebhookReceived, "Processing queued webhook for shop {Shop} | Topic: {Topic}", job.ShopDomain, job.Topic);

                    using var scope = _scopeFactory.CreateScope();
                    await ProcessJobAsync(scope.ServiceProvider, job, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(LogEventIds.WebhookProcessingError, ex, "Error occurred processing queued webhook job.");
                }
            }

            _logger.LogInformation("Webhook Queue Processor background service stopped.");
        }

        private async Task ProcessJobAsync(IServiceProvider sp, WebhookJob job, CancellationToken ct)
        {
            var logger = sp.GetRequiredService<ILogger<WebhookQueueProcessor>>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            var tokenStore = sp.GetRequiredService<ISkypointTokenStore>();
            var apiClient = sp.GetRequiredService<ISkypointApiClient>();
            var shopTokenStore = sp.GetRequiredService<IShopTokenStore>();

            if (job.Topic.Equals("orders/create", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessOrderCreateAsync(job, tokenStore, apiClient, configuration, logger, ct);
            }
            else if (job.Topic.Equals("orders/updated", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(LogEventIds.WebhookProcessed, "Processed order updated in background. Payload: {Body}", job.Payload);
            }
            else if (job.Topic.Equals("orders/cancelled", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(LogEventIds.WebhookProcessed, "Processed order cancelled in background. Payload: {Body}", job.Payload);
            }
            else if (job.Topic.Equals("app/uninstalled", StringComparison.OrdinalIgnoreCase))
            {
                shopTokenStore.RemoveToken(job.ShopDomain);
                logger.LogInformation(LogEventIds.WebhookProcessed, "Processed app/uninstalled in background. Token removed for: {Shop}", job.ShopDomain);
            }
        }

        private async Task ProcessOrderCreateAsync(
            WebhookJob job, 
            ISkypointTokenStore tokenStore, 
            ISkypointApiClient apiClient, 
            IConfiguration configuration,
            ILogger logger,
            CancellationToken ct)
        {
            try
            {
                var shopifyOrder = JsonSerializer.Deserialize<ShopifyOrderWebhook>(job.Payload);
                if (shopifyOrder == null)
                {
                    logger.LogError("Failed to deserialize Shopify order payload.");
                    return;
                }

                var orderIdString = shopifyOrder.id.ToString();

                // Deduplication check
                if (_webhookQueue.ProcessedOrderBookings.ContainsKey(orderIdString))
                {
                    logger.LogInformation("Shopify order {OrderId} already processed. Skipping background run.", shopifyOrder.id);
                    return;
                }

                var shop = job.ShopDomain;

                // Resolve tokens
                var token = tokenStore.GetToken(shop);
                var skypointUserId = tokenStore.GetUserId(shop);
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(skypointUserId))
                {
                    var creds = tokenStore.GetCredentials(shop);
                    if (creds == null)
                    {
                        logger.LogError("No Skypoint credentials found for shop {Shop} during background processing.", shop);
                        return;
                    }

                    var loginResponse = await apiClient.LoginAsync(new LoginRequest
                    {
                        Username = creds.Value.username,
                        Pwd = creds.Value.password
                    });

                    if (loginResponse?.Token?.TokenValue == null)
                    {
                        logger.LogError("Failed to login to Skypoint API for shop {Shop} during background processing.", shop);
                        return;
                    }

                    token = loginResponse.Token.TokenValue;
                    skypointUserId = loginResponse.Id;
                    tokenStore.SaveToken(shop, token, loginResponse.Token.Expiration, skypointUserId);
                }

                // Map order using shared mapper in Infrastructure
                var bookingRequest = SkypointOrderMapper.MapShopifyOrderToSkypointBooking(shopifyOrder, skypointUserId, configuration);

                // Create booking with Polly protection active on the client
                var bookingResponse = await apiClient.CreateBookingAsync(bookingRequest, token);

                if (bookingResponse != null)
                {
                    _webhookQueue.ProcessedOrderBookings[orderIdString] = bookingResponse;
                    logger.LogInformation(LogEventIds.WebhookProcessed, "Successfully created background Skypoint booking for Shopify order {OrderId}", shopifyOrder.id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(LogEventIds.WebhookProcessingError, ex, "Exception occurred during background order create webhook processing.");
            }
        }
    }
}
