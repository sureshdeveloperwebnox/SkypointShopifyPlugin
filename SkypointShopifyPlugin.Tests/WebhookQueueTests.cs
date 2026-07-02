using System.Threading;
using System.Threading.Tasks;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Infrastructure.Services;
using Xunit;

namespace SkypointShopifyPlugin.Tests
{
    public class WebhookQueueTests
    {
        [Fact]
        public async Task QueueAndDequeue_PreservesFifoOrder()
        {
            // Arrange
            var queue = new WebhookQueue();
            var job1 = new WebhookJob { ShopDomain = "shop1.myshopify.com", Topic = "orders/create", Payload = "payload1" };
            var job2 = new WebhookJob { ShopDomain = "shop2.myshopify.com", Topic = "orders/updated", Payload = "payload2" };

            // Act
            await queue.QueueWebhookAsync(job1);
            await queue.QueueWebhookAsync(job2);

            var result1 = await queue.DequeueWebhookAsync(CancellationToken.None);
            var result2 = await queue.DequeueWebhookAsync(CancellationToken.None);

            // Assert
            Assert.Equal("shop1.myshopify.com", result1.ShopDomain);
            Assert.Equal("orders/create", result1.Topic);
            Assert.Equal("payload1", result1.Payload);

            Assert.Equal("shop2.myshopify.com", result2.ShopDomain);
            Assert.Equal("orders/updated", result2.Topic);
            Assert.Equal("payload2", result2.Payload);
        }

        [Fact]
        public void ProcessedOrderBookings_AllowsDeduplicationCaching()
        {
            // Arrange
            var queue = new WebhookQueue();
            var booking = new BookingResponse { Id = "booking-123", TrackNo = "TRK-123", Status = "Created" };

            // Act
            queue.ProcessedOrderBookings["12345678"] = booking;

            // Assert
            Assert.True(queue.ProcessedOrderBookings.ContainsKey("12345678"));
            Assert.False(queue.ProcessedOrderBookings.ContainsKey("87654321"));
            Assert.Equal("booking-123", queue.ProcessedOrderBookings["12345678"].Id);
        }
    }
}
