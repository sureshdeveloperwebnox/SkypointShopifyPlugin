using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Infrastructure.Services;
using SkypointShopifyPlugin.WebAPI.Controllers;
using Xunit;

namespace SkypointShopifyPlugin.Tests
{
    public class SkypointSettingsTests
    {
        [Fact]
        public void SkypointShopifySettings_HasDefaultValues()
        {
            var settings = new SkypointShopifySettings();
            Assert.True(settings.IsUat);
            Assert.True(settings.EnableRoad);
            Assert.True(settings.EnableAir);
            Assert.True(settings.EnableCounter);
            Assert.Equal("EXPRESS ROAD", settings.RenameRoad);
            Assert.Equal(1, settings.SortRoad);
            Assert.Equal("percent", settings.MarkupType);
            Assert.Equal(0m, settings.MarkupRoad);
            Assert.Equal(0m, settings.FallbackCost);
            Assert.Equal(0m, settings.FreeshipThreshold);
        }

        [Fact]
        public async Task SkypointSettingsService_ParsesMetafieldsCorrectly()
        {
            // Arrange
            var shop = "test-store.myshopify.com";
            var token = "access_token_123";

            var tokenStoreMock = new Mock<IShopTokenStore>();
            tokenStoreMock.Setup(x => x.GetToken(shop)).Returns(token);

            var metafieldsResponse = new
            {
                data = new
                {
                    shop = new
                    {
                        metafields = new
                        {
                            edges = new[]
                            {
                                new { node = new { key = "username", value = "api_user" } },
                                new { node = new { key = "password", value = "api_pass" } },
                                new { node = new { key = "isuat", value = "false" } },
                                new { node = new { key = "enable_road", value = "true" } },
                                new { node = new { key = "enable_air", value = "false" } },
                                new { node = new { key = "rename_road", value = "Road Renamed" } },
                                new { node = new { key = "markup_road", value = "10.5" } },
                                new { node = new { key = "markup_type", value = "flat" } },
                                new { node = new { key = "fallback_cost", value = "150.00" } },
                                new { node = new { key = "freeship_threshold", value = "1000.00" } }
                            }
                        }
                    }
                }
            };

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(metafieldsResponse), Encoding.UTF8, "application/json")
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var logger = NullLogger<SkypointSettingsService>.Instance;

            var settingsService = new SkypointSettingsService(httpClient, tokenStoreMock.Object, memoryCache, logger);

            // Act
            var settings = await settingsService.GetSettingsAsync(shop);

            // Assert
            Assert.NotNull(settings);
            Assert.Equal("api_user", settings.Username);
            Assert.Equal("api_pass", settings.Password);
            Assert.False(settings.IsUat);
            Assert.True(settings.EnableRoad);
            Assert.False(settings.EnableAir);
            Assert.Equal("Road Renamed", settings.RenameRoad);
            Assert.Equal(10.5m, settings.MarkupRoad);
            Assert.Equal("flat", settings.MarkupType);
            Assert.Equal(150.00m, settings.FallbackCost);
            Assert.Equal(1000.00m, settings.FreeshipThreshold);
        }

        [Fact]
        public async Task CarrierServiceController_FiltersRenamesAndMarkupsRates()
        {
            // Arrange
            var shop = "test-store.myshopify.com";
            
            var settings = new SkypointShopifySettings
            {
                Username = "user",
                Password = "pwd",
                IsUat = true,
                EnableRoad = true,
                EnableAir = false, // should filter out air
                RenameRoad = "SkyPoint Road Courier",
                MarkupType = "percent",
                MarkupRoad = 10m, // 10% markup
                FreeshipThreshold = 500m
            };

            var settingsServiceMock = new Mock<ISkypointSettingsService>();
            settingsServiceMock.Setup(x => x.GetSettingsAsync(shop)).ReturnsAsync(settings);

            var tokenStoreMock = new Mock<ISkypointTokenStore>();
            tokenStoreMock.Setup(x => x.GetToken(shop)).Returns("cached_token");

            var apiClientMock = new Mock<ISkypointApiClient>();
            apiClientMock
                .Setup(x => x.GetRatesAsync(It.IsAny<RateRequest>(), "cached_token", "https://uat.skypoint.online"))
                .ReturnsAsync(new List<RateResponse>
                {
                    new RateResponse { ServiceName = "EXPRESS ROAD", Price = 100.00, TransitDays = 3 },
                    new RateResponse { ServiceName = "EXPRESS AIR", Price = 200.00, TransitDays = 1 }
                });

            var configMock = new Mock<IConfiguration>();
            var logger = NullLogger<CarrierServiceController>.Instance;
            var shopifyAdminServiceMock = new Mock<IShopifyAdminService>();
            var shopTokenStoreMock = new Mock<IShopTokenStore>();

            var controller = new CarrierServiceController(
                logger, 
                apiClientMock.Object, 
                tokenStoreMock.Object, 
                configMock.Object, 
                settingsServiceMock.Object,
                shopifyAdminServiceMock.Object,
                shopTokenStoreMock.Object);

            var requestPayload = new CarrierServiceRequest
            {
                Rate = new ShopifyRatePayload
                {
                    Origin = new ShopifyAddress { City = "Germiston", PostalCode = "1401" },
                    Destination = new ShopifyAddress { City = "Cape Town", PostalCode = "8000" },
                    Items = new List<ShopifyItem>
                    {
                        new ShopifyItem { Name = "Book", Quantity = 1, Price = 15000 } // 150 ZAR
                    },
                    Currency = "ZAR"
                }
            };

            // Set up context
            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString($"?shop={shop}");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            // Act
            // Mock Request Body
            var rawBody = JsonSerializer.Serialize(requestPayload);
            var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(rawBody));
            httpContext.Request.Body = stream;

            var response = await controller.GetRates();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(response);
            var carrierResp = Assert.IsType<CarrierServiceResponse>(okResult.Value);
            
            // Only Road should remain because Air was disabled in settings
            Assert.Single(carrierResp.Rates);
            var roadRate = carrierResp.Rates.First();
            Assert.Equal("SkyPoint Road Courier", roadRate.ServiceName);
            Assert.Equal("EXPRESS ROAD", roadRate.ServiceCode);
            // 100.00 + 10% markup = 110.00 ZAR = 11000 cents
            Assert.Equal(11000, roadRate.TotalPrice);
        }

        [Fact]
        public async Task TestRegisterMetafieldDefinitions()
        {
            var shop = "teststore-hzegetac.myshopify.com";
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "SkypointShopifyPlugin.WebAPI", "data");
            if (!Directory.Exists(dataDir))
            {
                dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
            }

            var keyFile = Path.Combine(dataDir, "skypoint_key.bin");
            if (!File.Exists(keyFile)) return; // Skip if run in a clean CI environment

            var configDict = new Dictionary<string, string>();
            var realKeyBytes = File.ReadAllBytes(keyFile);
            configDict["EncryptionKey"] = Convert.ToBase64String(realKeyBytes);

            var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();

            var logger = NullLogger<EncryptionService>.Instance;
            var encryptionService = new EncryptionService(config, logger);

            var tokensFile = Path.Combine(dataDir, "shopify_tokens.json");
            var json = File.ReadAllText(tokensFile);
            var tokens = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            var record = tokens[shop];
            var encToken = record.GetProperty("EncryptedToken").GetString();
            var iv = record.GetProperty("IV").GetString();

            var decryptedToken = encryptionService.Decrypt(encToken, iv);

            var logMessages = new List<string>();
            var adminLoggerMock = new Mock<ILogger<ShopifyAdminService>>();
            adminLoggerMock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()))
                .Callback<LogLevel, EventId, object, Exception, Delegate>((level, id, state, ex, formatter) =>
                {
                    logMessages.Add(state.ToString());
                });

            var httpClient = new HttpClient();
            var adminService = new ShopifyAdminService(httpClient, config, adminLoggerMock.Object, new MemoryCache(new MemoryCacheOptions()));

            var result = await adminService.RegisterMetafieldDefinitionsAsync(shop, decryptedToken);
            Assert.True(result.success, "REGISTRATION_FAILED: " + result.message + "\nLOGS:\n" + string.Join("\n", logMessages));
        }
    }
}
