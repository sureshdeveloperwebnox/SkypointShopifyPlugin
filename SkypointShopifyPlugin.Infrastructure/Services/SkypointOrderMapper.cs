using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Service for mapping Skypoint orders to booking requests
    /// Mirrors the mapping logic used in ShopifyController and ShopifyOrderPollingService
    /// </summary>
    public static class SkypointOrderMapper
    {
        /// <summary>
        /// Maps a SkypointOrder to a BookingRequest for the Skypoint API
        /// </summary>
        public static BookingRequest MapToBookingRequest(SkypointOrder order, string skypointUserId)
        {
            var shippingAddress = order.ShippingAddress;
            var billingAddress = order.BillingAddress;
            var customer = order.Customer;
            
            var pickupDate = order.CreatedAt == default ? DateTime.UtcNow : order.CreatedAt;
            var customerEmail = FirstNonEmpty(customer?.Email, string.Empty);
            var dropOffPhone = FirstNonEmpty(shippingAddress?.Phone, customer?.Phone, " ");
            var pickUpPhone = FirstNonEmpty(billingAddress?.Phone, customer?.Phone, " ");
            var parcelType = "A4_Text_Book";
            
            var lineItems = order.LineItems?.Count > 0
                ? order.LineItems
                : new List<SkypointOrderItem> { new() { Sku = order.OrderNumber, Quantity = 1 } };

            return new BookingRequest
            {
                UserId = skypointUserId,
                PickUpAddress = FirstNonEmpty(billingAddress?.Address1, " "),
                DropOffAddress = FirstNonEmpty(shippingAddress?.Address1, " "),
                FromSuburb = FirstNonEmpty(billingAddress?.City, " "),
                ToSuburb = FirstNonEmpty(shippingAddress?.City, " "),
                PickUpPCode = FirstNonEmpty(billingAddress?.Zip, " "),
                DropOffPCode = FirstNonEmpty(shippingAddress?.Zip, " "),
                Comment = $"@PickUp: Skypoint Order #{order.OrderNumber} @DropOff: No comment",
                Province = FirstNonEmpty(billingAddress?.Province, " "),
                DestinationProvince = FirstNonEmpty(shippingAddress?.Province, " "),
                DropOff = new DropOffPerson
                {
                    FirstName = FirstNonEmpty(shippingAddress?.FirstName, customer?.FirstName, " "),
                    LastName = FirstNonEmpty(shippingAddress?.LastName, customer?.LastName, " "),
                    Phone = dropOffPhone,
                    Email = customerEmail,
                    Suburb = FirstNonEmpty(shippingAddress?.City, " "),
                    City = FirstNonEmpty(shippingAddress?.City, " "),
                    State = FirstNonEmpty(shippingAddress?.Province, " "),
                    Zip = FirstNonEmpty(shippingAddress?.Zip, " ")
                },
                PickUp = new PickUpPerson
                {
                    FirstName = FirstNonEmpty(billingAddress?.FirstName, customer?.FirstName, " "),
                    LastName = FirstNonEmpty(billingAddress?.LastName, customer?.LastName, " "),
                    Phone = pickUpPhone,
                    Email = customerEmail,
                    Suburb = FirstNonEmpty(billingAddress?.City, " "),
                    City = FirstNonEmpty(billingAddress?.City, " "),
                    State = FirstNonEmpty(billingAddress?.Province, " "),
                    Zip = FirstNonEmpty(billingAddress?.Zip, " ")
                },
                Type = "ROAD",
                PickUpDate = pickupDate.ToString("dd"),
                PickUpTime = pickupDate.ToString("HH:mm"),
                ParcelDimensions = lineItems.Select(item => new ParcelDimension
                {
                    ParcelMass = 5.0,
                    ParcelLength = 30.0,
                    ParcelBreadth = 30.0,
                    ParcelHeight = 23.0,
                    PredefinedParcel = parcelType,
                    ParcelReference = FirstNonEmpty(item.Sku, item.Title, order.OrderNumber),
                    SelectedParcel = parcelType
                }).ToList(),
                PickUpCity = FirstNonEmpty(billingAddress?.City, " "),
                DropOffCity = FirstNonEmpty(shippingAddress?.City, " "),
                PickUpZip = FirstNonEmpty(billingAddress?.Zip, " "),
                DropOffZip = FirstNonEmpty(shippingAddress?.Zip, " "),
                ShipmentType = string.Empty,
                ToCounterCode = string.Empty,
                ToCounterName = string.Empty,
                SaIdNumber = string.Empty,
                PickUpCountry = FirstNonEmpty(billingAddress?.Country, string.Empty)
            };
        }

        /// <summary>
        /// Maps a CreateSkypointOrderRequest to a SkypointOrder
        /// </summary>
        public static SkypointOrder MapToSkypointOrder(CreateSkypointOrderRequest request)
        {
            return new SkypointOrder
            {
                Id = Guid.NewGuid().ToString(),
                OrderNumber = request.OrderNumber,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                FinancialStatus = request.FinancialStatus,
                FulfillmentStatus = "unfulfilled",
                Customer = request.Customer,
                LineItems = request.LineItems,
                ShippingAddress = request.ShippingAddress,
                BillingAddress = request.BillingAddress,
                TotalPrice = request.TotalPrice,
                Currency = request.Currency,
                Status = "pending",
                OrderSource = "skypoint",
                VendorId = request.VendorId
            };
        }

        /// <summary>
        /// Updates a SkypointOrder with booking response data
        /// </summary>
        public static void UpdateWithBookingResponse(SkypointOrder order, BookingResponse bookingResponse)
        {
            order.SkypointBookingId = bookingResponse.Id;
            order.SkypointTrackNo = bookingResponse.TrackNo;
            order.SkypointStatus = bookingResponse.Status;
            order.Status = "processing";
            order.UpdatedAt = DateTime.UtcNow;
        }

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
