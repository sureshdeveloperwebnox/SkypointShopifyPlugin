# Skypoint Shopify Plugin

A Shopify integration plugin built with ASP.NET Core that connects Shopify stores to the Skypoint shipping API. This plugin follows Clean Architecture principles with centralized configuration for API endpoints.

## Architecture

The solution is organized into four layers following Clean Architecture:

- **SkypointShopifyPlugin.Core** - Domain entities, DTOs, interfaces, and configuration settings
- **SkypointShopifyPlugin.Application** - Business logic with MediatR CQRS pattern
- **SkypointShopifyPlugin.Infrastructure** - External service integrations (HTTP client for Skypoint API)
- **SkypointShopifyPlugin.WebAPI** - REST API with Shopify OAuth and webhook endpoints

## Features

- **Centralized Configuration**: All API endpoints and settings managed in `appsettings.json`
- **Skypoint API Integration**: 
  - User authentication
  - Shipping rate quotes
  - Booking creation
- **Shopify Integration**:
  - OAuth authentication flow
  - Webhook handlers for orders (create, update, cancel)
  - App uninstallation handling
- **Clean Architecture**: Separation of concerns with dependency injection
- **MediatR**: CQRS pattern for command/query handling
- **Swagger/OpenAPI**: API documentation

## Prerequisites

- .NET 9.0 SDK
- Shopify Partner Account (for app credentials)
- Skypoint API credentials

## Setup

### 1. Configuration

Update `appsettings.json` with your credentials:

```json
{
  "SkypointApi": {
    "BaseUrl": "https://uat.skypoint.online",
    "LoginEndpoint": "/api/service/session/customer/login",
    "RateEndpoint": "/api/service/rate/engine/quote",
    "BookingEndpoint": "/api/service/booking/create",
    "TimeoutSeconds": 30
  },
  "Shopify": {
    "ClientId": "YOUR_SHOPIFY_CLIENT_ID",
    "ClientSecret": "YOUR_SHOPIFY_CLIENT_SECRET",
    "Scopes": "read_orders,write_orders,read_products,write_products,read_shipping,write_shipping",
    "RedirectUri": "https://your-domain.com/api/shopify/auth",
    "WebhookSecret": "YOUR_WEBHOOK_SECRET"
  }
}
```

### 2. Shopify App Configuration

Update `shopify.app.toml` with your Shopify app details:

```toml
client_id = "YOUR_SHOPIFY_CLIENT_ID"
name = "Skypoint Shipping"
handle = "skypoint-shipping"
application_url = "https://your-domain.com"
```

### 3. Build and Run

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run the WebAPI
cd SkypointShopifyPlugin.WebAPI
dotnet run
```

The API will be available at `https://localhost:5001` (or the port configured in your launch settings).

## API Endpoints

### Skypoint API Endpoints

- `POST /api/skypoint/login` - Authenticate with Skypoint
- `POST /api/skypoint/rates` - Get shipping rates
- `POST /api/skypoint/booking` - Create a shipping booking

### Shopify Webhook Endpoints

- `GET /api/shopify` - Shopify app install endpoint
- `GET /api/shopify/auth` - OAuth callback endpoint
- `POST /api/shopify/orders/create` - Order creation webhook
- `POST /api/shopify/orders/updated` - Order update webhook
- `POST /api/shopify/orders/cancelled` - Order cancellation webhook
- `POST /api/shopify/app/uninstalled` - App uninstallation webhook

## API Documentation

Swagger UI is available in development mode at:
```
https://localhost:5001/swagger
```

## Project Structure

```
SkypointShopifyPlugin/
├── SkypointShopifyPlugin.Core/
│   ├── Configuration/          # Centralized settings
│   ├── DTOs/Skypoint/         # Skypoint API DTOs
│   └── Interfaces/            # Service interfaces
├── SkypointShopifyPlugin.Application/
│   ├── Features/Skypoint/
│   │   └── Commands/         # MediatR commands/handlers
│   └── Common/                # Shared utilities
├── SkypointShopifyPlugin.Infrastructure/
│   ├── Services/              # External service implementations
│   └── DependencyInjection/   # DI configuration
├── SkypointShopifyPlugin.WebAPI/
│   ├── Controllers/           # API controllers
│   └── Program.cs             # Application entry point
└── shopify.app.toml           # Shopify app configuration
```

## Centralized Base URL Configuration

The Skypoint API base URL is configured in `SkypointShopifyPlugin.Core/Configuration/SkypointApiSettings.cs` and can be overridden in `appsettings.json`. This ensures all API calls use the same base URL consistently across the application.

```csharp
public class SkypointApiSettings
{
    public const string SectionName = "SkypointApi";
    public string BaseUrl { get; set; } = "https://uat.skypoint.online";
    public string LoginEndpoint { get; set; } = "/api/service/session/customer/login";
    public string RateEndpoint { get; set; } = "/api/service/rate/engine/quote";
    public string BookingEndpoint { get; set; } = "/api/service/booking/create";
    public int TimeoutSeconds { get; set; } = 30;
}
```

## Development

### Adding New Skypoint API Endpoints

1. Add DTOs in `SkypointShopifyPlugin.Core/DTOs/Skypoint/`
2. Add endpoint configuration in `SkypointApiSettings.cs`
3. Add interface method in `ISkypointApiClient.cs`
4. Implement in `SkypointApiClient.cs`
5. Create MediatR command/handler in `Application/Features/Skypoint/Commands/`
6. Add controller endpoint in `SkypointController.cs`

### Adding New Shopify Webhooks

1. Add webhook configuration in `shopify.app.toml`
2. Add endpoint in `ShopifyController.cs`
3. Implement webhook processing logic

## License

This project is proprietary software.
