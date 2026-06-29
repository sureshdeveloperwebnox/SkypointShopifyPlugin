using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.DTOs.Shopify;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services;

/// <summary>
/// Background service that polls Shopify Admin API for new orders
/// This bypasses Shopify's protected customer data webhook restrictions
/// </summary>
public class ShopifyOrderPollingService : BackgroundService
{
    private readonly ILogger<ShopifyOrderPollingService> _logger;
    private readonly IShopifyAdminService _shopifyAdminService;
    private readonly IShopTokenStore _shopTokenStore;
    private readonly ISkypointTokenStore _skypointTokenStore;
    private readonly SkypointApiClient _skypointApiClient;
    private readonly ShopifyPollingOptions _options;

    private readonly Dictionary<string, DateTime> _lastProcessedTimes = new();
    private readonly object _lock = new();

    public ShopifyOrderPollingService(
        ILogger<ShopifyOrderPollingService> logger,
        IShopifyAdminService shopifyAdminService,
        IShopTokenStore shopTokenStore,
        ISkypointTokenStore skypointTokenStore,
        SkypointApiClient skypointApiClient,
        IOptions<ShopifyPollingOptions> options)
    {
        _logger = logger;
        _shopifyAdminService = shopifyAdminService;
        _shopTokenStore = shopTokenStore;
        _skypointTokenStore = skypointTokenStore;
        _skypointApiClient = skypointApiClient;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Shopify Order Polling Service started");
        _logger.LogInformation("Polling interval: {Interval} seconds", _options.PollingIntervalSeconds);

        // Initial delay before first poll
        await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollForOrdersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during order polling");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Shopify Order Polling Service stopped");
    }

    private async Task PollForOrdersAsync(CancellationToken stoppingToken)
    {
        var shops = _shopTokenStore.GetAllShops();
        _logger.LogInformation("Polling for orders from {Count} shop(s)", shops.Count);

        foreach (var shop in shops)
        {
            try
            {
                await PollShopForOrdersAsync(shop, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling shop {Shop}", shop);
            }
        }
    }

    private async Task PollShopForOrdersAsync(string shop, CancellationToken stoppingToken)
    {
        var accessToken = _shopTokenStore.GetToken(shop);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("No access token for shop {Shop}, skipping", shop);
            return;
        }

        // Get the last processed time for this shop
        var lastProcessed = GetLastProcessedTime(shop);

        // Fetch orders from Shopify Admin API
        var ordersJson = await _shopifyAdminService.GetOrdersJsonAsync(shop, accessToken, lastProcessed);

        if (string.IsNullOrEmpty(ordersJson) || ordersJson == "[]")
        {
            _logger.LogDebug("No new orders for shop {Shop}", shop);
            return;
        }

        var jsonDoc = JsonDocument.Parse(ordersJson);
        var ordersElement = jsonDoc.RootElement.GetProperty("orders");
        
        var orders = new List<ShopifyOrderWebhook>();
        foreach (var orderElement in ordersElement.EnumerateArray())
        {
            var orderJson = orderElement.GetRawText();
            var order = JsonSerializer.Deserialize<ShopifyOrderWebhook>(orderJson);
            if (order != null)
            {
                orders.Add(order);
            }
        }

        _logger.LogInformation("Found {Count} new order(s) for shop {Shop}", orders.Count, shop);

        // Process each order
        foreach (var order in orders)
        {
            try
            {
                await ProcessOrderAsync(order, shop, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order {OrderId} for shop {Shop}", order.id, shop);
            }
        }

        // Update the last processed time
        UpdateLastProcessedTime(shop, DateTime.UtcNow);
    }

    private async Task ProcessOrderAsync(ShopifyOrderWebhook order, string shop, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing order {OrderId} for shop {Shop}", order.id, shop);

        // Get or refresh Skypoint token
        var token = _skypointTokenStore.GetToken(shop);
        var skypointUserId = _skypointTokenStore.GetUserId(shop);

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(skypointUserId))
        {
            var creds = _skypointTokenStore.GetCredentials(shop);
            if (creds == null)
            {
                _logger.LogError("No Skypoint credentials for shop {Shop}", shop);
                return;
            }

            var loginResponse = await _skypointApiClient.LoginAsync(new LoginRequest
            {
                Username = creds.Value.username,
                Pwd = creds.Value.password
            });

            if (loginResponse?.Token?.TokenValue == null)
            {
                _logger.LogError("Failed to login to Skypoint for shop {Shop}", shop);
                return;
            }

            token = loginResponse.Token.TokenValue;
            skypointUserId = loginResponse.Id;
            _skypointTokenStore.SaveToken(shop, token, loginResponse.Token.Expiration, skypointUserId);
        }

        // Convert to booking request
        var bookingRequest = MapShopifyOrderToSkypointBooking(order, skypointUserId);

        // Create booking
        var bookingResponse = await _skypointApiClient.CreateBookingAsync(bookingRequest, token);

        if (bookingResponse != null)
        {
            _logger.LogInformation(
                "Successfully created Skypoint booking {TrackNo} for Shopify order {OrderId}",
                bookingResponse.TrackNo,
                order.id);
        }
        else
        {
            _logger.LogError("Failed to create Skypoint booking for order {OrderId}", order.id);
        }
    }

    private DateTime GetLastProcessedTime(string shop)
    {
        lock (_lock)
        {
            return _lastProcessedTimes.TryGetValue(shop, out var time) ? time : DateTime.MinValue;
        }
    }

    private void UpdateLastProcessedTime(string shop, DateTime time)
    {
        lock (_lock)
        {
            _lastProcessedTimes[shop] = time;
        }
    }

    private BookingRequest MapShopifyOrderToSkypointBooking(ShopifyOrderWebhook order, string skypointUserId)
    {
        var shippingAddress = order.shipping_address;
        var billingAddress = order.billing_address;
        var customer = order.customer;

        var customerEmail = customer?.email ?? "";
        var dropOffPhone = shippingAddress?.phone ?? customer?.phone ?? "";
        var pickUpPhone = customer?.phone ?? "";

        var pickupDate = order.created_at == default 
            ? DateTime.UtcNow 
            : order.created_at;

        var parcelType = "A4_Text_Book";

        var lineItems = order.line_items?.Count > 0
            ? order.line_items
            : new List<ShopifyLineItem> { new() { sku = order.order_number.ToString(), quantity = 1 } };

        return new BookingRequest
        {
            UserId = skypointUserId,
            PickUpAddress = FirstNonEmpty(billingAddress?.address1, " "),
            DropOffAddress = FirstNonEmpty(shippingAddress?.address1, " "),
            FromSuburb = FirstNonEmpty(billingAddress?.city, " "),
            ToSuburb = FirstNonEmpty(shippingAddress?.city, " "),
            PickUpPCode = FirstNonEmpty(billingAddress?.zip, " "),
            DropOffPCode = FirstNonEmpty(shippingAddress?.zip, " "),
            Comment = $"@PickUp: Shopify Order #{order.order_number} @DropOff: No comment",
            Province = FirstNonEmpty(billingAddress?.province, " "),
            DestinationProvince = FirstNonEmpty(shippingAddress?.province, " "),
            DropOff = new DropOffPerson
            {
                FirstName = FirstNonEmpty(shippingAddress?.first_name, customer?.first_name, " "),
                LastName = FirstNonEmpty(shippingAddress?.last_name, customer?.last_name, " "),
                Phone = dropOffPhone,
                Email = customerEmail,
                Suburb = FirstNonEmpty(shippingAddress?.city, " "),
                City = FirstNonEmpty(shippingAddress?.city, " "),
                State = FirstNonEmpty(shippingAddress?.province, " "),
                Zip = FirstNonEmpty(shippingAddress?.zip, " ")
            },
            PickUp = new PickUpPerson
            {
                FirstName = FirstNonEmpty(billingAddress?.first_name, customer?.first_name, " "),
                LastName = FirstNonEmpty(billingAddress?.last_name, customer?.last_name, " "),
                Phone = pickUpPhone,
                Email = customerEmail,
                Suburb = FirstNonEmpty(billingAddress?.city, " "),
                City = FirstNonEmpty(billingAddress?.city, " "),
                State = FirstNonEmpty(billingAddress?.province, " "),
                Zip = FirstNonEmpty(billingAddress?.zip, " ")
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
                ParcelReference = FirstNonEmpty(item.sku, item.title, order.order_number.ToString()),
                SelectedParcel = parcelType
            }).ToList(),
            PickUpCity = FirstNonEmpty(billingAddress?.city, " "),
            DropOffCity = FirstNonEmpty(shippingAddress?.city, " "),
            PickUpZip = FirstNonEmpty(billingAddress?.zip, " "),
            DropOffZip = FirstNonEmpty(shippingAddress?.zip, " "),
            ShipmentType = string.Empty,
            ToCounterCode = string.Empty,
            ToCounterName = string.Empty,
            SaIdNumber = string.Empty,
            PickUpCountry = FirstNonEmpty(billingAddress?.country, string.Empty)
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}

/// <summary>
/// Configuration options for Shopify order polling
/// </summary>
public class ShopifyPollingOptions
{
    public const string SectionName = "ShopifyPolling";

    /// <summary>
    /// How often to poll for new orders (in seconds)
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 60; // Default: every minute

    /// <summary>
    /// Initial delay before first poll (in seconds)
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 30; // Default: 30 seconds
}
