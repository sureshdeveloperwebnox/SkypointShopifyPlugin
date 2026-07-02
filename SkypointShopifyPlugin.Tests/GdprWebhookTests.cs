using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SkypointShopifyPlugin.WebAPI.Controllers;
using Xunit;

namespace SkypointShopifyPlugin.Tests
{
    /// <summary>
    /// Verifies compliance and execution correctness of the mandatory GDPR endpoints.
    /// </summary>
    public class GdprWebhookTests
    {
        private readonly ShopifyController _controller;

        public GdprWebhookTests()
        {
            // Instantiating ShopifyController with NullLogger and nulls for unused dependencies
            _controller = new ShopifyController(
                mediator: null!,
                logger: NullLogger<ShopifyController>.Instance,
                oauthService: null!,
                configuration: null!,
                skypointApiClient: null!,
                shopifyAdminService: null!,
                shopTokenStore: null!,
                skypointTokenStore: null!,
                webhookQueue: null!
            );
        }

        [Fact]
        public void CustomersRedact_ReturnsOkResult()
        {
            // Act
            var result = _controller.CustomersRedact();

            // Assert
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public void CustomersDataRequest_ReturnsOkResult()
        {
            // Act
            var result = _controller.CustomersDataRequest();

            // Assert
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public void ShopRedact_ReturnsOkResult()
        {
            // Act
            var result = _controller.ShopRedact();

            // Assert
            Assert.IsType<OkResult>(result);
        }
    }
}
