namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    /// <summary>
    /// Represents a Skypoint online booking order
    /// Mirrors the ShopifyOrderWebhook structure for consistency
    /// </summary>
    public class SkypointOrder
    {
        public string Id { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string FinancialStatus { get; set; } = "pending"; // pending, paid, partially_paid, refunded
        public string FulfillmentStatus { get; set; } = "unfulfilled"; // unfulfilled, partial, fulfilled
        public SkypointCustomer? Customer { get; set; }
        public List<SkypointOrderItem> LineItems { get; set; } = new();
        public SkypointAddress? ShippingAddress { get; set; }
        public SkypointAddress? BillingAddress { get; set; }
        public decimal TotalPrice { get; set; }
        public string Currency { get; set; } = "ZAR";
        public string Status { get; set; } = "pending"; // pending, processing, completed, cancelled
        public string? SkypointBookingId { get; set; }
        public string? SkypointTrackNo { get; set; }
        public string? SkypointStatus { get; set; }
        public string OrderSource { get; set; } = "skypoint"; // skypoint, shopify, woocommerce
        public string? VendorId { get; set; }
    }

    public class SkypointCustomer
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class SkypointOrderItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string? ProductId { get; set; }
        public string? VariantId { get; set; }
    }

    public class SkypointAddress
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Address1 { get; set; } = string.Empty;
        public string Address2 { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for creating a Skypoint order via API
    /// </summary>
    public class CreateSkypointOrderRequest
    {
        public string OrderNumber { get; set; } = string.Empty;
        public SkypointCustomer Customer { get; set; } = new();
        public List<SkypointOrderItem> LineItems { get; set; } = new();
        public SkypointAddress ShippingAddress { get; set; } = new();
        public SkypointAddress BillingAddress { get; set; } = new();
        public decimal TotalPrice { get; set; }
        public string Currency { get; set; } = "ZAR";
        public string FinancialStatus { get; set; } = "pending";
        public string? VendorId { get; set; }
    }

    /// <summary>
    /// Response DTO for Skypoint order operations
    /// </summary>
    public class SkypointOrderResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public SkypointOrder? Order { get; set; }
        public string? SkypointBookingId { get; set; }
        public string? SkypointTrackNo { get; set; }
    }

    /// <summary>
    /// Filter parameters for listing Skypoint orders
    /// </summary>
    public class SkypointOrderFilter
    {
        public string? VendorId { get; set; }
        public string? Status { get; set; }
        public string? OrderSource { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? SearchTerm { get; set; } // Search by order number, email, etc。
        public int Page { get; set; } = 1;
        public int Limit { get; set; } = 50;
        public string SortBy { get; set; } = "created_at";
        public string SortOrder { get; set; } = "desc";
    }

    /// <summary>
    /// Paginated response for order listings
    /// </summary>
    public class SkypointOrderListResponse
    {
        public bool Success { get; set; }
        public List<SkypointOrder> Orders { get; set; } = new();
        public PaginationInfo Pagination { get; set; } = new();
    }

    public class PaginationInfo
    {
        public int Page { get; set; }
        public int Limit { get; set; }
        public int Total { get; set; }
        public int TotalPages { get; set; }
    }
}
