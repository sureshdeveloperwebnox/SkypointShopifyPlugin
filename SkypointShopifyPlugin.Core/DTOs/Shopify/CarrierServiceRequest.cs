using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Shopify
{
    /// <summary>
    /// Shopify carrier service callback payload.
    /// Shopify sends: { "rate": { "origin": {...}, "destination": {...}, "items": [...], "currency": "INR", ... } }
    /// </summary>
    public class CarrierServiceRequest
    {
        [JsonPropertyName("rate")]
        public ShopifyRatePayload Rate { get; set; } = new();

        // Convenience accessors so controller code reads cleanly
        [JsonIgnore] public ShopifyAddress Origin => Rate.Origin;
        [JsonIgnore] public ShopifyAddress Destination => Rate.Destination;
        [JsonIgnore] public List<ShopifyItem> Items => Rate.Items;
        [JsonIgnore] public string CurrencyCode => Rate.Currency ?? "INR";
    }

    public class ShopifyRatePayload
    {
        [JsonPropertyName("origin")]
        public ShopifyAddress Origin { get; set; } = new();

        [JsonPropertyName("destination")]
        public ShopifyAddress Destination { get; set; } = new();

        [JsonPropertyName("items")]
        public List<ShopifyItem> Items { get; set; } = new();

        /// <summary>Shopify sends currency as a plain string e.g. "INR", "USD"</summary>
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("locale")]
        public string? Locale { get; set; }
    }

    public class ShopifyAddress
    {
        [JsonPropertyName("country")]
        public string Country { get; set; } = string.Empty;

        [JsonPropertyName("postal_code")]
        public string PostalCode { get; set; } = string.Empty;

        [JsonPropertyName("province")]
        public string Province { get; set; } = string.Empty;

        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("address1")]
        public string Address1 { get; set; } = string.Empty;

        [JsonPropertyName("address2")]
        public string Address2 { get; set; } = string.Empty;

        [JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;

        [JsonPropertyName("company_name")]
        public string CompanyName { get; set; } = string.Empty;
    }

    public class ShopifyItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("sku")]
        public string Sku { get; set; } = string.Empty;

        [JsonPropertyName("grams")]
        public double Grams { get; set; }

        [JsonPropertyName("price")]
        public double Price { get; set; }

        [JsonPropertyName("requires_shipping")]
        public bool RequiresShipping { get; set; }

        [JsonPropertyName("taxable")]
        public bool Taxable { get; set; }

        [JsonPropertyName("fulfillment_service")]
        public string FulfillmentService { get; set; } = string.Empty;

        [JsonPropertyName("product_id")]
        public long ProductId { get; set; }

        [JsonPropertyName("variant_id")]
        public long VariantId { get; set; }
    }

    // Legacy aliases so other code compiles without changes
    public class Item : ShopifyItem { }
    public class Origin : ShopifyAddress { }
    public class Destination : ShopifyAddress { }
    public class Currency { public string Code { get; set; } = string.Empty; public double Rate { get; set; } }
    public class Locale { public string Code { get; set; } = string.Empty; }
    public class Country { public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; }
    public class Property { public string Name { get; set; } = string.Empty; public string Value { get; set; } = string.Empty; }
    public class Rate { public string Id { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; }
}
