using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Service for mapping Skypoint orders and Shopify webhook orders to booking requests.
    /// Centrally maintains mapping and postcode mappings for the Skypoint API.
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
                ToCounterCode = FirstNonEmpty(order.ToCounterCode, string.Empty),
                ToCounterName = FirstNonEmpty(order.ToCounterName, string.Empty),
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
        /// Maps a ShopifyWebhook order payload directly to a Skypoint BookingRequest.
        /// Used by the background WebhookQueueProcessor.
        /// </summary>
        public static BookingRequest MapShopifyOrderToSkypointBooking(ShopifyOrderWebhook shopifyOrder, string skypointUserId, IConfiguration configuration)
        {
            var shippingAddress = shopifyOrder.shipping_address;
            var billingAddress = shopifyOrder.billing_address;
            var customer = shopifyOrder.customer;
            var pickupDate = shopifyOrder.created_at == default ? DateTime.UtcNow : shopifyOrder.created_at;
            var customerEmail = FirstNonEmpty(customer?.email, shopifyOrder.email);
            var dropOffPhone = FirstNonEmpty(shippingAddress?.phone, customer?.phone, " ");
            var pickUpPhone = FirstNonEmpty(customer?.phone, shippingAddress?.phone, " ");
            var parcelType = "A4_Text_Book";
            var lineItems = shopifyOrder.line_items.Count > 0
                ? shopifyOrder.line_items
                : new List<ShopifyLineItem> { new() { sku = shopifyOrder.order_number.ToString(), quantity = 1 } };

            // Map postal codes and suburbs to Skypoint-recognized values
            var pickupPCode = MapPostalCode(FirstNonEmpty(billingAddress?.zip, " "), FirstNonEmpty(billingAddress?.city, " "), configuration);
            var dropOffPCode = MapPostalCode(FirstNonEmpty(shippingAddress?.zip, " "), FirstNonEmpty(shippingAddress?.city, " "), configuration);
            var fromSuburb = MapSuburb(FirstNonEmpty(billingAddress?.city, " "), pickupPCode, configuration);
            var toSuburb = MapSuburb(FirstNonEmpty(shippingAddress?.city, " "), dropOffPCode, configuration);

            return new BookingRequest
            {
                UserId = skypointUserId,
                PickUpAddress = FirstNonEmpty(billingAddress?.address1, " "),
                DropOffAddress = FirstNonEmpty(shippingAddress?.address1, " "),
                FromSuburb = fromSuburb,
                ToSuburb = toSuburb,
                PickUpPCode = pickupPCode,
                DropOffPCode = dropOffPCode,
                Comment = $"@PickUp: Shopify Order #{shopifyOrder.order_number} @DropOff: No comment",
                Province = FirstNonEmpty(billingAddress?.province, " "),
                DestinationProvince = FirstNonEmpty(shippingAddress?.province, " "),
                DropOff = new DropOffPerson
                {
                    FirstName = FirstNonEmpty(shippingAddress?.first_name, customer?.first_name, " "),
                    LastName = FirstNonEmpty(shippingAddress?.last_name, customer?.last_name, " "),
                    Phone = dropOffPhone,
                    Email = customerEmail,
                    Suburb = toSuburb,
                    City = FirstNonEmpty(shippingAddress?.city, " "),
                    State = FirstNonEmpty(shippingAddress?.province, " "),
                    Zip = dropOffPCode
                },
                PickUp = new PickUpPerson
                {
                    FirstName = FirstNonEmpty(billingAddress?.first_name, customer?.first_name, " "),
                    LastName = FirstNonEmpty(billingAddress?.last_name, customer?.last_name, " "),
                    Phone = pickUpPhone,
                    Email = customerEmail,
                    Suburb = fromSuburb,
                    City = FirstNonEmpty(billingAddress?.city, " "),
                    State = FirstNonEmpty(billingAddress?.province, " "),
                    Zip = pickupPCode
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
                    ParcelReference = FirstNonEmpty(item.sku, item.title, shopifyOrder.order_number.ToString()),
                    SelectedParcel = parcelType
                }).ToList(),
                PickUpCity = FirstNonEmpty(billingAddress?.city, " "),
                DropOffCity = FirstNonEmpty(shippingAddress?.city, " "),
                PickUpZip = FirstNonEmpty(billingAddress?.zip, " "),
                DropOffZip = FirstNonEmpty(shippingAddress?.zip, " "),
                ShipmentType = string.Empty,
                ToCounterCode = shopifyOrder.note_attributes?.FirstOrDefault(attr => attr.name.Equals("pudo_code", StringComparison.OrdinalIgnoreCase) || attr.name.Equals("to_counter_code", StringComparison.OrdinalIgnoreCase))?.value ?? string.Empty,
                ToCounterName = shopifyOrder.note_attributes?.FirstOrDefault(attr => attr.name.Equals("pudo_name", StringComparison.OrdinalIgnoreCase) || attr.name.Equals("to_counter_name", StringComparison.OrdinalIgnoreCase))?.value ?? string.Empty,
                SaIdNumber = string.Empty,
                PickUpCountry = FirstNonEmpty(billingAddress?.country, string.Empty)
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

        public static SkypointOrder MapShopifyOrderToSkypointOrder(ShopifyOrderWebhook shopifyOrder, string shopDomain)
        {
            // Extract PUDO note attributes if present
            var toCounterCode = shopifyOrder.note_attributes?.FirstOrDefault(attr => attr.name.Equals("pudo_code", StringComparison.OrdinalIgnoreCase) || attr.name.Equals("to_counter_code", StringComparison.OrdinalIgnoreCase))?.value;
            var toCounterName = shopifyOrder.note_attributes?.FirstOrDefault(attr => attr.name.Equals("pudo_name", StringComparison.OrdinalIgnoreCase) || attr.name.Equals("to_counter_name", StringComparison.OrdinalIgnoreCase))?.value;
            var pudoAddr1 = shopifyOrder.note_attributes?.FirstOrDefault(attr => attr.name.Equals("pudo_addr1", StringComparison.OrdinalIgnoreCase))?.value;
            var pudoCity = shopifyOrder.note_attributes?.FirstOrDefault(attr => attr.name.Equals("pudo_city", StringComparison.OrdinalIgnoreCase))?.value;
            var pudoZip = shopifyOrder.note_attributes?.FirstOrDefault(attr => attr.name.Equals("pudo_zip", StringComparison.OrdinalIgnoreCase))?.value;
            var pudoProvider = shopifyOrder.note_attributes?.FirstOrDefault(attr => attr.name.Equals("pudo_provider", StringComparison.OrdinalIgnoreCase))?.value;

            return new SkypointOrder
            {
                Id = shopifyOrder.id.ToString(),
                OrderNumber = shopifyOrder.order_number.ToString(),
                CreatedAt = shopifyOrder.created_at == default ? DateTime.UtcNow : shopifyOrder.created_at,
                UpdatedAt = shopifyOrder.updated_at == default ? DateTime.UtcNow : shopifyOrder.updated_at,
                FinancialStatus = shopifyOrder.financial_status,
                FulfillmentStatus = shopifyOrder.fulfillment_status,
                TotalPrice = shopifyOrder.total_price,
                Currency = shopifyOrder.currency,
                Status = "pending",
                OrderSource = "shopify",
                VendorId = shopDomain,
                ToCounterCode = toCounterCode,
                ToCounterName = toCounterName,
                PudoAddress1 = pudoAddr1,
                PudoCity = pudoCity,
                PudoZip = pudoZip,
                PudoProvider = pudoProvider,
                Customer = shopifyOrder.customer == null ? null : new SkypointCustomer
                {
                    Id = shopifyOrder.customer.id.ToString(),
                    Email = shopifyOrder.customer.email,
                    FirstName = shopifyOrder.customer.first_name,
                    LastName = shopifyOrder.customer.last_name,
                    Phone = shopifyOrder.customer.phone
                },
                LineItems = shopifyOrder.line_items.Select(item => new SkypointOrderItem
                {
                    Id = item.id.ToString(),
                    Title = item.title,
                    Quantity = item.quantity,
                    Price = item.price,
                    Sku = item.sku
                }).ToList(),
                ShippingAddress = shopifyOrder.shipping_address == null ? null : new SkypointAddress
                {
                    FirstName = shopifyOrder.shipping_address.first_name,
                    LastName = shopifyOrder.shipping_address.last_name,
                    Address1 = shopifyOrder.shipping_address.address1,
                    Address2 = shopifyOrder.shipping_address.address2,
                    City = shopifyOrder.shipping_address.city,
                    Province = shopifyOrder.shipping_address.province,
                    Country = shopifyOrder.shipping_address.country,
                    Zip = shopifyOrder.shipping_address.zip,
                    Phone = shopifyOrder.shipping_address.phone
                },
                BillingAddress = shopifyOrder.billing_address == null ? null : new SkypointAddress
                {
                    FirstName = shopifyOrder.billing_address.first_name,
                    LastName = shopifyOrder.billing_address.last_name,
                    Address1 = shopifyOrder.billing_address.address1,
                    Address2 = shopifyOrder.billing_address.address2,
                    City = shopifyOrder.billing_address.city,
                    Province = shopifyOrder.billing_address.province,
                    Country = shopifyOrder.billing_address.country,
                    Zip = shopifyOrder.billing_address.zip
                }
            };
        }

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        private static string MapPostalCode(string postalCode, string suburb, IConfiguration configuration)
        {
            var code = postalCode?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(suburb))
            {
                var suburbKey = suburb.Replace(" ", "");
                var mappedCode = configuration[$"Skypoint:PostalCodeMappings:{suburbKey}"];
                if (!string.IsNullOrEmpty(mappedCode))
                    return mappedCode;
            }

            if (code.StartsWith("2") || suburb?.Contains("Johannesburg", StringComparison.OrdinalIgnoreCase) == true)
                return configuration["Skypoint:PostalCodeMappings:Johannesburg"] ?? "2000";

            if (code.StartsWith("0") || suburb?.Contains("Pretoria", StringComparison.OrdinalIgnoreCase) == true)
                return configuration["Skypoint:PostalCodeMappings:Pretoria"] ?? "0002";

            if (code.StartsWith("7") || code.StartsWith("8") || suburb?.Contains("Cape Town", StringComparison.OrdinalIgnoreCase) == true)
                return configuration["Skypoint:PostalCodeMappings:CapeTown"] ?? "8000";

            if (code.StartsWith("4") || suburb?.Contains("Durban", StringComparison.OrdinalIgnoreCase) == true)
                return configuration["Skypoint:PostalCodeMappings:Durban"] ?? "4001";

            if (code.StartsWith("9") || suburb?.Contains("Bloemfontein", StringComparison.OrdinalIgnoreCase) == true)
                return configuration["Skypoint:PostalCodeMappings:Bloemfontein"] ?? "9301";

            if (code.StartsWith("6") || suburb?.Contains("Port Elizabeth", StringComparison.OrdinalIgnoreCase) == true)
                return configuration["Skypoint:PostalCodeMappings:PortElizabeth"] ?? "6001";

            return code;
        }

        private static string MapSuburb(string suburb, string postalCode, IConfiguration configuration)
        {
            var sub = suburb?.Trim() ?? string.Empty;

            var suburbKey = sub.Replace(" ", "");
            var mappedSuburb = configuration[$"Skypoint:SuburbMappings:{suburbKey}"];
            if (!string.IsNullOrEmpty(mappedSuburb))
                return mappedSuburb;

            if (postalCode == "2000" || postalCode == configuration["Skypoint:PostalCodeMappings:Johannesburg"])
            {
                if (sub.Contains("Johannesburg", StringComparison.OrdinalIgnoreCase))
                    return "Johannesburg";
                if (sub.Contains("Germiston", StringComparison.OrdinalIgnoreCase))
                    return configuration["Skypoint:SuburbMappings:Germiston"] ?? "Johannesburg";
                return "Johannesburg";
            }

            if (postalCode == "0002" || postalCode == configuration["Skypoint:PostalCodeMappings:Pretoria"])
            {
                if (sub.Contains("Pretoria", StringComparison.OrdinalIgnoreCase))
                    return "Pretoria";
                return "Pretoria";
            }

            if (postalCode == "8000" || postalCode == configuration["Skypoint:PostalCodeMappings:CapeTown"])
            {
                if (sub.Contains("Cape Town", StringComparison.OrdinalIgnoreCase))
                    return "Cape Town";
                return "Cape Town";
            }

            if (postalCode == "4001" || postalCode == configuration["Skypoint:PostalCodeMappings:Durban"])
            {
                if (sub.Contains("Durban", StringComparison.OrdinalIgnoreCase))
                    return "Durban";
                return "Durban";
            }

            if (postalCode == "9301" || postalCode == configuration["Skypoint:PostalCodeMappings:Bloemfontein"])
            {
                if (sub.Contains("Bloemfontein", StringComparison.OrdinalIgnoreCase))
                    return "Bloemfontein";
                return "Bloemfontein";
            }

            return sub;
        }
    }
}
