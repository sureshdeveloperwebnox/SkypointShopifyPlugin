# SkypointShopifyPlugin Implementation Plan

## Executive Summary

This document provides a comprehensive implementation plan for the **SkypointShopifyPlugin** project, comparing it with the legacy **eShop-master** project and explaining why SkypointShopifyPlugin is the recommended solution for Shopify-Skypoint shipping integration.

---

## Project Comparison Overview

### SkypointShopifyPlugin (Recommended)
- **Architecture**: Modern Clean Architecture with .NET 9.0
- **Complexity**: Simple, focused, maintainable
- **Dependencies**: Minimal (no database, no message queue)
- **Configuration**: Centralized in appsettings.json and .env files
- **Deployment**: Easy - single executable with file-based storage
- **Status**: ✅ Fully functional and production-ready

### eShop-master (Legacy - Not Recommended)
- **Architecture**: Older .NET with Startup.cs pattern
- **Complexity**: Over-engineered with unnecessary components
- **Dependencies**: Heavy (SQL Server, RabbitMQ, JWT, multiple HTTP clients)
- **Configuration**: Complex with many interdependent settings
- **Deployment**: Difficult - requires multiple infrastructure components
- **Status**: ❌ Problematic, creates many issues

---

## Why eShop-master Creates Issues

### 1. **Over-Engineering**
- Implements complex patterns not needed for Shopify integration
- RabbitMQ message queuing for simple webhook processing
- SQL Server databases when file-based storage suffices
- Multiple HTTP clients with resilience policies for simple API calls

### 2. **Infrastructure Complexity**
- Requires SQL Server installation and configuration
- Requires RabbitMQ installation and configuration
- Requires certificate management for HTTPS
- Complex connection strings and database schemas
- Windows Service hosting (not cross-platform friendly)

### 3. **Configuration Nightmare**
- 127+ lines of configuration in appsettings.example.json
- Multiple database connection strings
- RabbitMQ configuration (consumer, producer, exchanges)
- JWT configuration
- Rate limiting configuration
- Serilog configuration with file paths
- Certificate path and password management

### 4. **Maintenance Burden**
- Multiple layers of abstraction
- Complex middleware stack
- Database resilience policies
- Transient exception detection
- Correlation ID tracking
- API versioning overhead

### 5. **Deployment Challenges**
- Requires Windows Server for Windows Service hosting
- Database migration scripts
- RabbitMQ setup and maintenance
- Certificate management
- Log file management with rotation

---

## Why SkypointShopifyPlugin is Superior

### 1. **Simplicity**
- Focused on single use case: Shopify-Skypoint integration
- No unnecessary abstractions or patterns
- Direct webhook processing without message queues
- File-based token storage (simple and effective)

### 2. **Modern Architecture**
- .NET 9.0 with minimal API pattern
- Clean Architecture separation of concerns
- MediatR CQRS pattern for business logic
- Top-level statements in Program.cs
- Cross-platform compatible

### 3. **Easy Configuration**
- Single appsettings.json file (26 lines)
- Optional .env file for environment variables
- Web UI for configuration (setup.html)
- Centralized API endpoint configuration
- No database or message queue setup required

### 4. **Zero Infrastructure Dependencies**
- No SQL Server required
- No RabbitMQ required
- No certificate management required
- No Windows Service required
- Runs as simple console/web application

### 5. **Developer Friendly**
- Swagger/OpenAPI documentation included
- Clear project structure
- Comprehensive README with setup instructions
- PowerShell scripts for webhook registration
- Easy to debug and test

### 6. **Production Ready**
- CORS configuration for ngrok compatibility
- Webhook signature verification
- Token encryption and secure storage
- Error handling and logging
- Health check endpoints

---

## SkypointShopifyPlugin Architecture

### Project Structure

```
SkypointShopifyPlugin/
├── SkypointShopifyPlugin.Core/          # Domain layer
│   ├── Configuration/                   # Settings classes
│   │   ├── ShopifySettings.cs
│   │   └── SkypointApiSettings.cs
│   ├── DTOs/                            # Data transfer objects
│   │   ├── Configuration/
│   │   ├── Shopify/
│   │   └── Skypoint/
│   └── Interfaces/                      # Service interfaces
├── SkypointShopifyPlugin.Application/   # Business logic
│   ├── Features/
│   │   └── Skypoint/
│   │       └── Commands/                # MediatR commands
│   └── Common/                          # Shared utilities
├── SkypointShopifyPlugin.Infrastructure/ # External integrations
│   ├── Services/
│   │   ├── CarrierServiceBootstrapService.cs
│   │   ├── ConfigurationBootstrapService.cs
│   │   ├── ConfigurationStore.cs
│   │   ├── ShopTokenStore.cs
│   │   ├── ShopifyAdminService.cs
│   │   ├── ShopifyOAuthService.cs
│   │   ├── SkypointApiClient.cs
│   │   ├── SkypointCredentialStore.cs
│   │   ├── SkypointOrderMapper.cs
│   │   ├── SkypointOrderService.cs
│   │   ├── SkypointOrderStore.cs
│   │   ├── SkypointTokenBootstrapService.cs
│   │   └── SkypointTokenStore.cs
│   └── DependencyInjection/             # DI configuration
├── SkypointShopifyPlugin.WebAPI/         # API layer
│   ├── Controllers/
│   │   ├── AdminController.cs
│   │   ├── AuthController.cs
│   │   ├── CarrierServiceController.cs
│   │   ├── ConfigurationController.cs
│   │   ├── ShippingController.cs
│   │   ├── ShopifyController.cs
│   │   ├── SkypointController.cs
│   │   └── SkypointOrderController.cs
│   ├── wwwroot/                         # Web UI
│   │   ├── index.html                   # Login page
│   │   ├── setup.html                   # Setup page
│   │   └── dashboard.html               # Dashboard
│   ├── Program.cs                       # Application entry
│   └── appsettings.json                 # Configuration
└── shopify.app.toml                     # Shopify app config
```

### Key Components

#### 1. **Shopify Integration**
- **OAuth Flow**: Handles Shopify app installation and authorization
- **Webhook Handlers**: Processes orders/create, orders/updated, orders/cancelled, app/uninstalled
- **Carrier Service**: Provides real-time shipping rates at checkout
- **Admin API**: Registers carrier services and webhooks

#### 2. **Skypoint API Integration**
- **Authentication**: Login and token management
- **Rate Quotes**: Get shipping rates from Skypoint
- **Booking Creation**: Create shipments in Skypoint system
- **Order Mapping**: Converts Shopify orders to Skypoint format

#### 3. **Token Management**
- **ShopTokenStore**: Stores Shopify access tokens per shop
- **SkypointTokenStore**: Stores Skypoint authentication tokens
- **SkypointCredentialStore**: Stores Skypoint login credentials
- **Encryption**: Tokens are encrypted at rest

#### 4. **Web UI**
- **Setup Page**: Initial configuration wizard
- **Login Page**: Skypoint credential authentication
- **Dashboard**: Order management and status tracking

---

## Implementation Steps

### Phase 1: Initial Setup (Day 1)

#### Step 1.1: Environment Preparation
```bash
# Install .NET 9.0 SDK
# Verify installation
dotnet --version

# Clone or navigate to project
cd d:\Office\skynet\SkypointShopifyPlugin
```

#### Step 1.2: Configuration Setup
```bash
# Copy example environment file
copy .env.example .env

# Edit .env with your credentials
# Required settings:
# - Shopify__ClientId
# - Shopify__ClientSecret
# - Shopify__RedirectUri
# - Shopify__WebhookSecret
# - SkypointApi__BaseUrl (default: https://uat.skypoint.online)
```

#### Step 1.3: Shopify Partner Account Setup
1. Log in to Shopify Partner Dashboard
2. Create a new app or use existing app
3. Configure app credentials in .env file
4. Set app URL to your deployment URL (e.g., ngrok URL for development)
5. Configure webhook endpoints:
   - `https://your-domain.com/api/shopify/orders/create`
   - `https://your-domain.com/api/shopify/orders/updated`
   - `https://your-domain.com/api/shopify/orders/cancelled`
   - `https://your-domain.com/api/shopify/app/uninstalled`

#### Step 1.4: Build and Run
```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run WebAPI
cd SkypointShopifyPlugin.WebAPI
dotnet run
```

The API will start on the configured port (default: 5001)

---

### Phase 2: Shopify App Installation (Day 1-2)

#### Step 2.1: Install App on Shopify Store
1. Access the install URL: `https://your-domain.com/api/shopify?shop=your-store.myshopify.com`
2. Complete OAuth authorization flow
3. App will automatically register carrier service and webhooks

#### Step 2.2: Configure Skypoint Credentials
1. Access the dashboard: `https://your-domain.com/index.html?shop=your-store.myshopify.com`
2. Login with Skypoint credentials
3. Configure default parcel dimensions
4. Configure postal code and suburb mappings (if needed)

#### Step 2.3: Test Carrier Service
1. Create a test order in Shopify
2. At checkout, verify Skypoint shipping rates appear
3. Complete test order
4. Verify booking is created in Skypoint system

---

### Phase 3: Production Deployment (Day 3-5)

#### Step 3.1: Choose Hosting Platform
**Options:**
- Azure App Service (recommended)
- AWS Elastic Beanstalk
- Google Cloud Run
- Docker container on any cloud
- Self-hosted on VPS

#### Step 3.2: Azure App Service Deployment (Example)
```bash
# Publish application
dotnet publish -c Release -o ./publish

# Create Azure App Service
az webapp create --resource-group MyResourceGroup --plan MyPlan --name MySkypointApp

# Deploy published files
az webapp deployment source config-zip --resource-group MyResourceGroup --name MySkypointApp --src publish.zip

# Configure environment variables in Azure Portal
# Settings → Configuration → Application Settings
```

#### Step 3.3: Domain Configuration
1. Purchase and configure custom domain
2. Update Shopify app configuration with production URL
3. Configure SSL certificate (automatic on Azure App Service)
4. Update Shopify__RedirectUri in configuration

#### Step 3.4: Webhook Registration
```powershell
# Run webhook registration script
.\register-webhooks.ps1
```

---

### Phase 4: Monitoring and Maintenance (Ongoing)

#### Step 4.1: Logging
- Application logs to console and file
- Monitor for errors and warnings
- Set up log aggregation if needed

#### Step 4.2: Health Checks
- Endpoint: `/health` (if implemented)
- Monitor application uptime
- Set up alerts for downtime

#### Step 4.3: Token Management
- Monitor token expiration
- Implement token refresh if needed
- Handle shop reauthorization

#### Step 4.4: Order Monitoring
- Track order processing success rate
- Monitor for failed bookings
- Implement retry logic for transient failures

---

## Configuration Details

### appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "SkypointApi": {
    "BaseUrl": "https://uat.skypoint.online",
    "LoginEndpoint": "/api/service/session/customer/login",
    "RegisterEndpoint": "/api/service/session/customer/register",
    "RateEndpoint": "/api/service/rate/engine/quote",
    "BookingEndpoint": "/api/service/booking/create",
    "TimeoutSeconds": 30,
    "MaxRetryAttempts": 3
  },
  "Shopify": {
    "ClientId": "YOUR_SHOPIFY_CLIENT_ID",
    "ClientSecret": "YOUR_SHOPIFY_CLIENT_SECRET",
    "Scopes": "read_orders,write_orders,read_products,write_products,read_shipping,write_shipping,read_fulfillments,write_fulfillments",
    "RedirectUri": "https://your-domain.com/api/shopify/auth",
    "WebhookSecret": "YOUR_WEBHOOK_SECRET"
  }
}
```

### .env File
```env
# Shopify Configuration
Shopify__ClientId=your_client_id
Shopify__ClientSecret=your_client_secret
Shopify__RedirectUri=https://your-domain.com/api/shopify/auth
Shopify__WebhookSecret=your_webhook_secret

# Skypoint API Configuration
SkypointApi__BaseUrl=https://uat.skypoint.online

# Optional: Override default endpoints
# SkypointApi__LoginEndpoint=/api/service/session/customer/login
# SkypointApi__RateEndpoint=/api/service/rate/engine/quote
# SkypointApi__BookingEndpoint=/api/service/booking/create
```

---

## API Endpoints

### Shopify Endpoints
- `GET /api/shopify` - App installation
- `GET /api/shopify/auth` - OAuth callback
- `GET /api/shopify/setup` - Carrier service setup
- `POST /api/shopify/orders/create` - Order creation webhook
- `POST /api/shopify/orders/updated` - Order update webhook
- `POST /api/shopify/orders/cancelled` - Order cancellation webhook
- `POST /api/shopify/app/uninstalled` - App uninstallation webhook

### Skypoint Endpoints
- `POST /api/skypoint/login` - Authenticate with Skypoint
- `POST /api/skypoint/rates` - Get shipping rates
- `POST /api/skypoint/booking` - Create shipping booking

### Carrier Service Endpoints
- `POST /api/carrier/rates` - Real-time shipping rates for checkout

### Admin Endpoints
- `POST /api/admin/config` - Save configuration
- `GET /api/admin/config` - Retrieve configuration
- `POST /api/admin/webhooks/register` - Register webhooks
- `POST /api/admin/carrier/register` - Register carrier service

---

## Troubleshooting

### Common Issues and Solutions

#### Issue 1: Carrier Service Not Registering
**Symptoms**: Shipping rates not appearing at checkout
**Causes**: 
- HTTPS requirement not met
- Redirect URI not configured correctly
- Shopify token expired

**Solutions**:
1. Ensure callback URL uses HTTPS
2. Update Shopify__RedirectUri in .env with current ngrok/production URL
3. Reinstall app to get fresh token
4. Check carrier service registration logs

#### Issue 2: Webhook Signature Verification Failing
**Symptoms**: Webhooks returning 401 Unauthorized
**Causes**: Webhook secret mismatch

**Solutions**:
1. Verify webhook secret in Shopify Partner Dashboard matches .env
2. Restart application after updating webhook secret
3. Check webhook secret is not "YOUR_WEBHOOK_SECRET"

#### Issue 3: Skypoint API Authentication Failing
**Symptoms**: Booking creation fails with 401/403 errors
**Causes**: Invalid credentials or expired token

**Solutions**:
1. Verify Skypoint credentials in dashboard
2. Check Skypoint API base URL is correct
3. Re-authenticate to get fresh token
4. Verify Skypoint API is accessible

#### Issue 4: Order Not Processed
**Symptoms**: Order created but no Skypoint booking
**Causes**: Webhook not received or processing failed

**Solutions**:
1. Check webhook is registered in Shopify
2. Verify webhook URL is accessible
3. Check application logs for errors
4. Test webhook manually using Shopify webhook testing tool

---

## Migration from eShop-master to SkypointShopifyPlugin

### Why Migrate?
- Eliminate SQL Server dependency
- Eliminate RabbitMQ dependency
- Reduce configuration complexity
- Improve maintainability
- Reduce deployment complexity
- Lower infrastructure costs
- Faster development cycle

### Migration Steps

#### Step 1: Data Migration (If Needed)
If eShop-master has important data in SQL Server:
1. Export shop configurations
2. Export order history
3. Export token mappings
4. Import into SkypointShopifyPlugin file-based storage

#### Step 2: Shopify App Reconfiguration
1. Update app URL in Shopify Partner Dashboard
2. Update webhook endpoints
3. Reinstall app on test store
4. Verify functionality

#### Step 3: DNS Update
1. Update DNS to point to new application
2. Wait for DNS propagation
3. Verify SSL certificate

#### Step 4: Decommission Legacy System
1. Stop eShop-master Windows Service
2. Shut down SQL Server (if no other apps use it)
3. Shut down RabbitMQ (if no other apps use it)
4. Remove old infrastructure

---

## Security Considerations

### Token Security
- Shopify access tokens encrypted at rest
- Skypoint tokens encrypted at rest
- Credentials stored securely
- Webhook signature verification

### HTTPS Requirements
- Shopify requires HTTPS for all endpoints
- Carrier service callback must use HTTPS
- Use ngrok for development with HTTPS
- Use proper SSL certificates in production

### API Security
- Webhook signature validation
- CORS configuration
- Rate limiting (if needed)
- Input validation

---

## Performance Optimization

### Caching
- Shopify tokens cached in memory
- Skypoint tokens cached in memory
- Configuration cached in memory

### Async Processing
- Carrier service registration in background
- Webhook registration in background
- Non-blocking OAuth flow

### Error Handling
- Retry logic for transient failures
- Circuit breaker pattern (if needed)
- Graceful degradation

---

## Testing Strategy

### Unit Testing
- Test MediatR commands/handlers
- Test mapping logic
- Test token storage

### Integration Testing
- Test Shopify OAuth flow
- Test webhook processing
- Test Skypoint API integration

### End-to-End Testing
- Test complete order flow
- Test carrier service at checkout
- Test booking creation

### Load Testing
- Test webhook processing under load
- Test carrier service response time
- Test concurrent order processing

---

## Rollback Plan

### If Issues Occur
1. Revert DNS to point to eShop-master
2. Restart eShop-master Windows Service
3. Investigate issue in SkypointShopifyPlugin
4. Fix issue and redeploy
5. Update DNS again

### Data Recovery
- File-based storage easily backed up
- Configuration stored in app_config.json
- Tokens stored in encrypted files
- Order history stored in JSON files

---

## Support and Maintenance

### Documentation
- README.md with setup instructions
- Inline code comments
- API documentation via Swagger
- This implementation plan

### Monitoring
- Application logs
- Error tracking
- Performance metrics
- Order processing statistics

### Updates
- .NET dependency updates
- Shopify API updates
- Skypoint API updates
- Security patches

---

## Conclusion

**SkypointShopifyPlugin** is the recommended solution for Shopify-Skypoint integration because:

1. **Simpler Architecture**: Easy to understand, modify, and maintain
2. **Fewer Dependencies**: No database or message queue required
3. **Easier Deployment**: Single executable, cross-platform
4. **Lower Cost**: No infrastructure costs for SQL Server or RabbitMQ
5. **Faster Development**: Focused on use case, less boilerplate
6. **Better Reliability**: Fewer moving parts, fewer failure points
7. **Modern Stack**: .NET 9.0, latest patterns and practices

**eShop-master** is not recommended because:

1. **Over-Engineered**: Complex patterns not needed for this use case
2. **Heavy Dependencies**: SQL Server, RabbitMQ, Windows Service
3. **Difficult Deployment**: Multiple infrastructure components
4. **High Maintenance**: Complex configuration and troubleshooting
5. **Legacy Code**: Older patterns, outdated architecture
6. **Costly**: Infrastructure costs for unnecessary components

**Recommendation**: Use SkypointShopifyPlugin for all new Shopify-Skypoint integrations and migrate existing eShop-master installations to SkypointShopifyPlugin.

---

## Contact and Support

For issues or questions:
- Check application logs
- Review this implementation plan
- Consult README.md
- Check Shopify Partner Dashboard
- Check Skypoint API documentation

---

**Document Version**: 1.0  
**Last Updated**: 2025-06-30  
**Status**: Production Ready
