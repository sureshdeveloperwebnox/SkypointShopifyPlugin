using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Shopify
{
    public class ShopifyOrderWebhook
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long id { get; set; }
        public string email { get; set; } = string.Empty;
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long order_number { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public string financial_status { get; set; } = string.Empty;
        public string fulfillment_status { get; set; } = string.Empty;
        public ShopifyCustomer? customer { get; set; }
        public List<ShopifyLineItem> line_items { get; set; } = new();
        public ShopifyShippingAddress? shipping_address { get; set; }
        public ShopifyBillingAddress? billing_address { get; set; }
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal total_price { get; set; }
        public string currency { get; set; } = string.Empty;
    }

    public class ShopifyCustomer
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long id { get; set; }
        public string email { get; set; } = string.Empty;
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
        public string phone { get; set; } = string.Empty;
    }

    public class ShopifyLineItem
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long id { get; set; }
        public string title { get; set; } = string.Empty;
        public int quantity { get; set; }
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal price { get; set; }
        public string sku { get; set; } = string.Empty;
    }

    public class ShopifyShippingAddress
    {
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
        public string address1 { get; set; } = string.Empty;
        public string address2 { get; set; } = string.Empty;
        public string city { get; set; } = string.Empty;
        public string province { get; set; } = string.Empty;
        public string country { get; set; } = string.Empty;
        public string zip { get; set; } = string.Empty;
        public string phone { get; set; } = string.Empty;
    }

    public class ShopifyBillingAddress
    {
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
        public string address1 { get; set; } = string.Empty;
        public string address2 { get; set; } = string.Empty;
        public string city { get; set; } = string.Empty;
        public string province { get; set; } = string.Empty;
        public string country { get; set; } = string.Empty;
        public string zip { get; set; } = string.Empty;
    }
}
