using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Collections.Concurrent;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Thread-safe bounded channel queue for background processing of webhooks.
    /// Configured for multiple writers (HTTP API requests) and a single reader (background service)
    /// to preserve Shopify event ordering and prevent race conditions.
    /// Includes a shared memory-backed concurrent cache for deduplication.
    /// </summary>
    public class WebhookQueue : IWebhookQueue
    {
        private readonly Channel<WebhookJob> _channel;
        public ConcurrentDictionary<string, BookingResponse> ProcessedOrderBookings { get; } = new(StringComparer.OrdinalIgnoreCase);

        public WebhookQueue()
        {
            var options = new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,  // Single reader guarantees FIFO processing for ordering stability
                SingleWriter = false  // Multiple concurrent API threads writing jobs
            };
            _channel = Channel.CreateBounded<WebhookJob>(options);
        }

        public async ValueTask QueueWebhookAsync(WebhookJob job)
        {
            await _channel.Writer.WriteAsync(job);
        }

        public async ValueTask<WebhookJob> DequeueWebhookAsync(CancellationToken cancellationToken)
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
    }
}
