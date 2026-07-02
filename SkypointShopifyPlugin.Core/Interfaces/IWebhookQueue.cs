using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    /// <summary>
    /// Thread-safe in-memory queue interface using System.Threading.Channels
    /// for high-throughput webhook digestion and shared processed cache.
    /// </summary>
    public interface IWebhookQueue
    {
        ValueTask QueueWebhookAsync(WebhookJob job);
        ValueTask<WebhookJob> DequeueWebhookAsync(CancellationToken cancellationToken);
        ConcurrentDictionary<string, BookingResponse> ProcessedOrderBookings { get; }
    }
}
