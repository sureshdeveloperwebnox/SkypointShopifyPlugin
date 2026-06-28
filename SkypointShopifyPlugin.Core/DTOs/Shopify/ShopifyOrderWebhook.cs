namespace SkypointShopifyPlugin.Core.DTOs.Shopify
{
    public class ShopifyOrderWebhook
    {
        public string id { get; set; } = string.Empty;
        public string email { get; set; } = string.Empty;
        public string order_number { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public string financial_status { get; set; } = string.Empty;
        public string fulfillment_status { get; set; } = string.Empty;
        public ShopifyCustomer? customer { get; set; }
        public List<ShopifyLineItem> line_items { get; set; } = new();
        public ShopifyShippingAddress? shipping_address { get; set; }
        public ShopifyBillingAddress? billing_address { get; set; }
        public string total_price { get; set; } = string.Empty;
        public string currency { get; set; } = string.Empty;
    }

    public class ShopifyCustomer
    {
        public string id { get; set; } = string.Empty;
        public string email { get; set; } = string.Empty;
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
        public string phone { get; set; } = string.Empty;
    }

    public class ShopifyLineItem
    {
        public string id { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
        public int quantity { get; set; }
        public string price { get; set; } = string.Empty;
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
