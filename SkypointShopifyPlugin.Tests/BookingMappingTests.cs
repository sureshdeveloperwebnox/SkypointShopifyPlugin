using System.Text.Json;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.WebAPI.Controllers;
using Xunit;

namespace SkypointShopifyPlugin.Tests
{
    public class BookingMappingTests
    {
        [Fact]
        public void MapShopifyOrderToSkypointBooking_MapsFieldsCorrectly()
        {
            // Arrange
            var shopifyOrder = new ShopifyOrderWebhook
            {
                id = "cc45a1fc-38ef-4cb3-a889-84ddbb5ef7b5",
                order_number = "108726",
                created_at = new DateTime(2026, 6, 26, 13, 11, 0, DateTimeKind.Utc),
                customer = new ShopifyCustomer
                {
                    id = "cc45a1fc-38ef-4cb3-a889-84ddbb5ef7b5",
                    first_name = "Swaathi",
                    last_name = "Mano",
                    phone = "0979013425",
                    email = "swaathi@myambergroup.com"
                },
                billing_address = new ShopifyBillingAddress
                {
                    first_name = "Swaathi",
                    last_name = "Mano",
                    address1 = "140 North Reef Road",
                    city = "GERMISTON",
                    province = "Gauteng",
                    country = "South Africa",
                    zip = "1401"
                },
                shipping_address = new ShopifyShippingAddress
                {
                    first_name = "Swaathi",
                    last_name = "Mano",
                    address1 = "123 Heatherdale Road",
                    city = "BLOEMFONTEIN",
                    province = "Free State",
                    country = "South Africa",
                    zip = "9301",
                    phone = "0979013425"
                },
                line_items = new List<ShopifyLineItem>
                {
                    new ShopifyLineItem
                    {
                        sku = "A4_Text_Book",
                        quantity = 1,
                        price = "105.00"
                    }
                }
            };

            // Act
            var bookingRequest = ShopifyController.MapShopifyOrderToSkypointBooking(shopifyOrder, "cc45a1fc-38ef-4cb3-a889-84ddbb5ef7b5");

            // Assert
            Assert.Equal("cc45a1fc-38ef-4cb3-a889-84ddbb5ef7b5", bookingRequest.UserId);
            Assert.Equal("140 North Reef Road", bookingRequest.PickUpAddress);
            Assert.Equal("123 Heatherdale Road", bookingRequest.DropOffAddress);
            Assert.Equal("GERMISTON", bookingRequest.FromSuburb);
            Assert.Equal("BLOEMFONTEIN", bookingRequest.ToSuburb);
            Assert.Equal("1401", bookingRequest.PickUpPCode);
            Assert.Equal("9301", bookingRequest.DropOffPCode);
            Assert.Equal("@PickUp: Shopify Order #108726 @DropOff: No comment", bookingRequest.Comment);
            Assert.Equal("Gauteng", bookingRequest.Province);
            Assert.Equal("Free State", bookingRequest.DestinationProvince);
            Assert.Equal("ROAD", bookingRequest.Type);
            Assert.Equal("26", bookingRequest.PickUpDate);
            Assert.Equal("13:11", bookingRequest.PickUpTime);
            Assert.Single(bookingRequest.ParcelDimensions);
            Assert.Equal(5.0, bookingRequest.ParcelDimensions[0].ParcelMass);
            Assert.Equal(30.0, bookingRequest.ParcelDimensions[0].ParcelLength);
            Assert.Equal(30.0, bookingRequest.ParcelDimensions[0].ParcelBreadth);
            Assert.Equal(23.0, bookingRequest.ParcelDimensions[0].ParcelHeight);
            Assert.Equal("A4_Text_Book", bookingRequest.ParcelDimensions[0].PredefinedParcel);
            Assert.Equal("A4_Text_Book", bookingRequest.ParcelDimensions[0].ParcelReference);
            Assert.Equal("A4_Text_Book", bookingRequest.ParcelDimensions[0].SelectedParcel);

            // Verify pickUp details
            Assert.Equal("Swaathi", bookingRequest.PickUp.FirstName);
            Assert.Equal("Mano", bookingRequest.PickUp.LastName);
            Assert.Equal("0979013425", bookingRequest.PickUp.Phone);
            Assert.Equal("swaathi@myambergroup.com", bookingRequest.PickUp.Email);
            Assert.Equal("GERMISTON", bookingRequest.PickUp.Suburb);
            Assert.Equal("1401", bookingRequest.PickUp.Zip);

            // Verify dropOff details
            Assert.Equal("Swaathi", bookingRequest.DropOff.FirstName);
            Assert.Equal("Mano", bookingRequest.DropOff.LastName);
            Assert.Equal("0979013425", bookingRequest.DropOff.Phone);
            Assert.Equal("swaathi@myambergroup.com", bookingRequest.DropOff.Email);
            Assert.Equal("BLOEMFONTEIN", bookingRequest.DropOff.Suburb);
            Assert.Equal("9301", bookingRequest.DropOff.Zip);
        }

        [Fact]
        public void BookingRequest_SerializesWithCorrectCaseProperties()
        {
            // Arrange
            var bookingRequest = new BookingRequest
            {
                UserId = "cc45a1fc-38ef-4cb3-a889-84ddbb5ef7b5",
                PickUpZip = "1401",
                DropOffZip = "9301",
                ParcelDimensions = new List<ParcelDimension>
                {
                    new()
                    {
                        ParcelMass = 5.0,
                        ParcelLength = 30.0,
                        ParcelBreadth = 30.0,
                        ParcelHeight = 23.0,
                        ParcelReference = "SKU-1",
                        SelectedParcel = "A4_Text_Book"
                    }
                }
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Act
            var json = JsonSerializer.Serialize(bookingRequest, options);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Assert
            Assert.True(root.TryGetProperty("userId", out _));
            Assert.True(root.TryGetProperty("parcelDimensions", out var parcelDimensions));
            var parcel = parcelDimensions.EnumerateArray().First();
            Assert.True(parcel.TryGetProperty("parcel_mass", out _));
            Assert.True(parcel.TryGetProperty("parcel_length", out _));
            Assert.True(parcel.TryGetProperty("parcel_breadth", out _));
            Assert.True(parcel.TryGetProperty("parcel_height", out _));
            Assert.True(parcel.TryGetProperty("parcel_reference", out _));
            Assert.True(parcel.TryGetProperty("selected_parcel", out _));
            
            // Critical case sensitivity assertions:
            // "pickUpzip" and "dropOffzip" must be exact, with lowercase 'z'
            Assert.True(root.TryGetProperty("pickUpzip", out var pickUpZipProp));
            Assert.False(root.TryGetProperty("pickUpZip", out _));
            Assert.Equal("1401", pickUpZipProp.GetString());

            Assert.True(root.TryGetProperty("dropOffzip", out var dropOffZipProp));
            Assert.False(root.TryGetProperty("dropOffZip", out _));
            Assert.Equal("9301", dropOffZipProp.GetString());
        }
    }
}
