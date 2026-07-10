using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using SkypointShopifyPlugin.Infrastructure.Services;
using Xunit;

namespace SkypointShopifyPlugin.Tests
{
    public class ShopifyAdminServiceTests
    {
        [Fact]
        public async Task AssignCarrier_WhenSouthAfricaZoneExists_UpdatesZone()
        {
            // Arrange
            var shop = "test-store.myshopify.com";
            var token = "access_token_123";
            var carrierServiceUrl = "https://example.com/rates";
            var carrierId = "8888";

            // Mock carrier service check: returns existing matching carrier
            var carrierResponse = new
            {
                carrier_services = new[]
                {
                    new { id = carrierId, name = "Skypoint Shipping", callback_url = carrierServiceUrl }
                }
            };

            // Mock delivery profiles query: ZA zone exists but lacks Skypoint method definition
            var profilesQueryResponse = new
            {
                data = new
                {
                    deliveryProfiles = new
                    {
                        edges = new[]
                        {
                            new
                            {
                                node = new
                                {
                                    id = "gid://shopify/DeliveryProfile/1",
                                    name = "General",
                                    profileLocationGroups = new[]
                                    {
                                        new
                                        {
                                            locationGroup = new { id = "gid://shopify/DeliveryLocationGroup/1" },
                                            locationGroupZones = new
                                            {
                                                edges = new[]
                                                {
                                                    new
                                                    {
                                                        node = new
                                                        {
                                                            zone = new
                                                            {
                                                                id = "gid://shopify/DeliveryZone/1",
                                                                name = "Domestic",
                                                                countries = new[]
                                                                {
                                                                    new { code = new { countryCode = "ZA" } }
                                                                }
                                                            },
                                                            methodDefinitions = new
                                                            {
                                                                edges = Array.Empty<object>()
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            // Mock update mutation response
            var mutationResponse = new
            {
                data = new
                {
                    deliveryProfileUpdate = new
                    {
                        profile = new { id = "gid://shopify/DeliveryProfile/1" },
                        userErrors = Array.Empty<object>()
                    }
                }
            };

            HttpRequestMessage? capturedMutationRequest = null;
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.AbsolutePath.EndsWith("carrier_services.json"))
                {
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(JsonSerializer.Serialize(carrierResponse), Encoding.UTF8, "application/json")
                    };
                }
                else if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.AbsolutePath.EndsWith("graphql.json"))
                {
                    var body = req.Content != null ? req.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult() : "";
                    if (body.Contains("deliveryProfiles"))
                    {
                        return new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK,
                            Content = new StringContent(JsonSerializer.Serialize(profilesQueryResponse), Encoding.UTF8, "application/json")
                        };
                    }
                    else
                    {
                        capturedMutationRequest = req;
                        return new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK,
                            Content = new StringContent(JsonSerializer.Serialize(mutationResponse), Encoding.UTF8, "application/json")
                        };
                    }
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handlerMock.Object);
            var config = new Mock<IConfiguration>().Object;
            var logger = NullLogger<ShopifyAdminService>.Instance;
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var adminService = new ShopifyAdminService(httpClient, config, logger, memoryCache);

            // Act
            var result = await adminService.RegisterAndAssignCarrierServiceAsync(shop, token, carrierServiceUrl);

            // Assert
            Assert.True(result.success);
            Assert.Contains("Added to 1 shipping zone(s) automatically", result.message);

            // Verify mutation payload
            Assert.NotNull(capturedMutationRequest);
            var requestBody = await capturedMutationRequest.Content.ReadAsStringAsync();
            Assert.Contains("zonesToUpdate", requestBody);
            Assert.Contains("gid://shopify/DeliveryZone/1", requestBody);
            Assert.Contains("participant", requestBody);
            Assert.Contains("carrierServiceId", requestBody);
            Assert.DoesNotContain("zonesToCreate", requestBody);
        }

        [Fact]
        public async Task AssignCarrier_WhenSouthAfricaZoneDoesNotExist_CreatesZone()
        {
            // Arrange
            var shop = "test-store.myshopify.com";
            var token = "access_token_123";
            var carrierServiceUrl = "https://example.com/rates";
            var carrierId = "8888";

            // Mock carrier service check: returns existing matching carrier
            var carrierResponse = new
            {
                carrier_services = new[]
                {
                    new { id = carrierId, name = "Skypoint Shipping", callback_url = carrierServiceUrl }
                }
            };

            // Mock delivery profiles query: ZA zone does NOT exist
            var profilesQueryResponse = new
            {
                data = new
                {
                    deliveryProfiles = new
                    {
                        edges = new[]
                        {
                            new
                            {
                                node = new
                                {
                                    id = "gid://shopify/DeliveryProfile/1",
                                    name = "General",
                                    profileLocationGroups = new[]
                                    {
                                        new
                                        {
                                            locationGroup = new { id = "gid://shopify/DeliveryLocationGroup/1" },
                                            locationGroupZones = new
                                            {
                                                edges = new[]
                                                {
                                                    new
                                                    {
                                                        node = new
                                                        {
                                                            zone = new
                                                            {
                                                                id = "gid://shopify/DeliveryZone/2",
                                                                name = "Rest of World",
                                                                countries = new[]
                                                                {
                                                                    new { code = new { countryCode = "US" } }
                                                                }
                                                            },
                                                            methodDefinitions = new
                                                            {
                                                                edges = Array.Empty<object>()
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            // Mock update mutation response
            var mutationResponse = new
            {
                data = new
                {
                    deliveryProfileUpdate = new
                    {
                        profile = new { id = "gid://shopify/DeliveryProfile/1" },
                        userErrors = Array.Empty<object>()
                    }
                }
            };

            HttpRequestMessage? capturedMutationRequest = null;
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.AbsolutePath.EndsWith("carrier_services.json"))
                {
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(JsonSerializer.Serialize(carrierResponse), Encoding.UTF8, "application/json")
                    };
                }
                else if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.AbsolutePath.EndsWith("graphql.json"))
                {
                    var body = req.Content != null ? req.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult() : "";
                    if (body.Contains("deliveryProfiles"))
                    {
                        return new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK,
                            Content = new StringContent(JsonSerializer.Serialize(profilesQueryResponse), Encoding.UTF8, "application/json")
                        };
                    }
                    else
                    {
                        capturedMutationRequest = req;
                        return new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK,
                            Content = new StringContent(JsonSerializer.Serialize(mutationResponse), Encoding.UTF8, "application/json")
                        };
                    }
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handlerMock.Object);
            var config = new Mock<IConfiguration>().Object;
            var logger = NullLogger<ShopifyAdminService>.Instance;
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var adminService = new ShopifyAdminService(httpClient, config, logger, memoryCache);

            // Act
            var result = await adminService.RegisterAndAssignCarrierServiceAsync(shop, token, carrierServiceUrl);

            // Assert
            Assert.True(result.success);
            Assert.Contains("Added to 1 shipping zone(s) automatically", result.message);

            // Verify mutation payload
            Assert.NotNull(capturedMutationRequest);
            var requestBody = await capturedMutationRequest.Content.ReadAsStringAsync();
            Assert.Contains("zonesToCreate", requestBody);
            Assert.Contains("South Africa", requestBody);
            Assert.Contains("ZA", requestBody);
            Assert.Contains("participant", requestBody);
            Assert.Contains("carrierServiceId", requestBody);
            Assert.DoesNotContain("zonesToUpdate", requestBody);
        }
    }
}
