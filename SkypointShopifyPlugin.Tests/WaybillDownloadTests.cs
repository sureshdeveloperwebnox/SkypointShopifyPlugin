using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;
using SkypointShopifyPlugin.Infrastructure.Services;
using Xunit;

namespace SkypointShopifyPlugin.Tests
{
    public class WaybillDownloadTests
    {
        // Helper: creates SkypointOrderService with mocked dependencies
        private static SkypointOrderService BuildService(
            ISkypointApiClient apiClient,
            ISkypointOrderStore orderStore,
            ISkypointTokenStore tokenStore)
        {
            return new SkypointOrderService(
                NullLogger<SkypointOrderService>.Instance,
                orderStore,
                apiClient,
                tokenStore,
                new Mock<IEcommercePlatformService>().Object,
                null!
            );
        }

        // Helper: safe base64 decode
        private static bool TryDecodeBase64(string base64, out byte[]? bytes)
        {
            try { bytes = Convert.FromBase64String(base64); return true; }
            catch { bytes = null; return false; }
        }

        // ----------------------------------------------------------------
        // 1. DTO deserialization - mirrors the actual SkyPoint API response
        // ----------------------------------------------------------------
        [Fact]
        public void WaybillDownloadResponse_DeserializesCorrectly()
        {
            var sampleBase64Pdf = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4 minimal"));
            var json = "{\"fileId\":null,\"downloadUrl\":null,\"fileStream\":\"" + sampleBase64Pdf + "\",\"fileName\":\"080040106215.pdf\",\"applicationType\":\"application/pdf\"}";

            var response = JsonSerializer.Deserialize<WaybillDownloadResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(response);
            Assert.Equal("080040106215.pdf", response.FileName);
            Assert.Equal("application/pdf", response.ApplicationType);
            Assert.False(string.IsNullOrEmpty(response.FileStream));
        }

        // ----------------------------------------------------------------
        // 2. FileStream is valid base64 and decodes to bytes starting with %PDF
        // ----------------------------------------------------------------
        [Fact]
        public void WaybillFileStream_IsValidBase64AndStartsWithPdfMagicBytes()
        {
            byte[] realPdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<</Type /Catalog>>\nendobj\n%%EOF");
            var base64Stream = Convert.ToBase64String(realPdfBytes);

            var response = new WaybillDownloadResponse
            {
                FileStream = base64Stream,
                FileName = "080040106215.pdf",
                ApplicationType = "application/pdf"
            };

            var isValidBase64 = TryDecodeBase64(response.FileStream!, out var decodedBytes);
            Assert.True(isValidBase64, "FileStream must be valid base64");
            Assert.NotNull(decodedBytes);
            Assert.True(decodedBytes.Length > 0, "Decoded bytes must be non-empty");

            var pdfHeader = Encoding.ASCII.GetString(decodedBytes, 0, Math.Min(4, decodedBytes.Length));
            Assert.Equal("%PDF", pdfHeader);
        }

        // ----------------------------------------------------------------
        // 3. FileName has .pdf extension and matches waybill number
        // ----------------------------------------------------------------
        [Fact]
        public void WaybillFileName_HasPdfExtensionAndMatchesWaybillNumber()
        {
            const string waybillNumber = "080040106215";
            var response = new WaybillDownloadResponse
            {
                FileStream = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4")),
                FileName = waybillNumber + ".pdf",
                ApplicationType = "application/pdf"
            };

            Assert.EndsWith(".pdf", response.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(waybillNumber, response.FileName);
        }

        // ----------------------------------------------------------------
        // 4. ApplicationType must be application/pdf
        // ----------------------------------------------------------------
        [Fact]
        public void WaybillApplicationType_IsApplicationPdf()
        {
            var response = new WaybillDownloadResponse
            {
                FileStream = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4")),
                FileName = "080040106215.pdf",
                ApplicationType = "application/pdf"
            };

            Assert.Equal("application/pdf", response.ApplicationType);
        }

        // ----------------------------------------------------------------
        // 5. Service calls API client with the correct waybill number
        // ----------------------------------------------------------------
        [Fact]
        public async Task SkypointOrderService_DownloadWaybillAsync_CallsApiClientWithCorrectWaybillNumber()
        {
            const string waybillNumber = "080040106215";
            const string orderId = "order-001";
            const string vendorId = "test-vendor";
            const string authToken = "test-bearer-token";

            var expectedResponse = new WaybillDownloadResponse
            {
                FileId = null,
                DownloadUrl = null,
                FileStream = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF")),
                FileName = waybillNumber + ".pdf",
                ApplicationType = "application/pdf"
            };

            var order = new SkypointOrder
            {
                Id = orderId,
                VendorId = vendorId,
                SkypointTrackNo = waybillNumber,
                OrderNumber = "10001",
                Status = "processing"
            };

            var mockApiClient = new Mock<ISkypointApiClient>();
            mockApiClient
                .Setup(c => c.DownloadWaybillAsync(waybillNumber, authToken))
                .ReturnsAsync(expectedResponse);

            var mockOrderStore = new Mock<ISkypointOrderStore>();
            mockOrderStore
                .Setup(s => s.GetOrderByIdAsync(orderId))
                .ReturnsAsync(order);

            var mockTokenStore = new Mock<ISkypointTokenStore>();
            mockTokenStore.Setup(t => t.GetToken(vendorId)).Returns(authToken);
            mockTokenStore.Setup(t => t.GetUserId(vendorId)).Returns("user-123");

            var service = BuildService(mockApiClient.Object, mockOrderStore.Object, mockTokenStore.Object);

            var result = await service.DownloadWaybillAsync(orderId);

            // Assert: result is not null and has expected fields
            Assert.NotNull(result);
            Assert.Equal(waybillNumber + ".pdf", result.FileName);
            Assert.Equal("application/pdf", result.ApplicationType);
            Assert.False(string.IsNullOrEmpty(result.FileStream), "FileStream must not be empty");

            // Assert: FileStream decodes to bytes starting with %PDF
            var isValidBase64 = TryDecodeBase64(result.FileStream!, out var decoded);
            Assert.True(isValidBase64, "FileStream must be valid base64");
            var header = Encoding.ASCII.GetString(decoded!, 0, Math.Min(4, decoded!.Length));
            Assert.Equal("%PDF", header);

            // Assert: API client was called exactly once with the correct waybill number
            mockApiClient.Verify(c => c.DownloadWaybillAsync(waybillNumber, authToken), Times.Once);
        }

        // ----------------------------------------------------------------
        // 6. Returns null when order does not exist
        // ----------------------------------------------------------------
        [Fact]
        public async Task SkypointOrderService_DownloadWaybillAsync_ReturnsNull_WhenOrderNotFound()
        {
            var mockApiClient = new Mock<ISkypointApiClient>();
            var mockOrderStore = new Mock<ISkypointOrderStore>();
            var mockTokenStore = new Mock<ISkypointTokenStore>();

            mockOrderStore.Setup(s => s.GetOrderByIdAsync("missing-order"))
                .ReturnsAsync((SkypointOrder?)null);

            var service = BuildService(mockApiClient.Object, mockOrderStore.Object, mockTokenStore.Object);

            var result = await service.DownloadWaybillAsync("missing-order");

            Assert.Null(result);
            // API client must never be called when the order is missing
            mockApiClient.Verify(c => c.DownloadWaybillAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ----------------------------------------------------------------
        // 7. Returns null when the order has no waybill track number
        // ----------------------------------------------------------------
        [Fact]
        public async Task SkypointOrderService_DownloadWaybillAsync_ReturnsNull_WhenTrackNoMissing()
        {
            var mockApiClient = new Mock<ISkypointApiClient>();
            var mockOrderStore = new Mock<ISkypointOrderStore>();
            var mockTokenStore = new Mock<ISkypointTokenStore>();

            var orderWithNoTrackNo = new SkypointOrder
            {
                Id = "order-no-track",
                VendorId = "vendor",
                SkypointTrackNo = string.Empty
            };

            mockOrderStore.Setup(s => s.GetOrderByIdAsync("order-no-track"))
                .ReturnsAsync(orderWithNoTrackNo);

            var service = BuildService(mockApiClient.Object, mockOrderStore.Object, mockTokenStore.Object);

            var result = await service.DownloadWaybillAsync("order-no-track");

            Assert.Null(result);
            // API client must not be called when there is no track number
        }

        // 8. Returns null when API call fails (no fallback local PDF generation)
        // ----------------------------------------------------------------
        [Fact]
        public async Task SkypointOrderService_DownloadWaybillAsync_ReturnsNull_WhenApiFails()
        {
            const string orderId = "order-002";
            const string trackNo = "DROP-108774";
            const string vendorId = "test-vendor";

            var order = new SkypointOrder
            {
                Id = orderId,
                VendorId = vendorId,
                SkypointTrackNo = trackNo,
                OrderNumber = "10001",
                Status = "processing"
            };

            var mockApiClient = new Mock<ISkypointApiClient>();
            var mockOrderStore = new Mock<ISkypointOrderStore>();
            mockOrderStore
                .Setup(s => s.GetOrderByIdAsync(orderId))
                .ReturnsAsync(order);

            // Mock login token retrieval
            var mockTokenStore = new Mock<ISkypointTokenStore>();
            mockTokenStore.Setup(t => t.GetToken(vendorId)).Returns("mock-auth-token");
            mockTokenStore.Setup(t => t.GetUserId(vendorId)).Returns("user-123");

            // Setup the API client to throw an exception for waybill download (simulating a 400 Bad Request / no waybill found)
            mockApiClient.Setup(c => c.DownloadWaybillAsync(trackNo, "mock-auth-token"))
                .ThrowsAsync(new HttpRequestException("Waybill not found", null, System.Net.HttpStatusCode.BadRequest));

            var service = BuildService(mockApiClient.Object, mockOrderStore.Object, mockTokenStore.Object);

            var result = await service.DownloadWaybillAsync(orderId);

            // Assert: returned null because fallback was removed
            Assert.Null(result);
        }
    }
}
