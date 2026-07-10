# SkypointShopifyPlugin - Complete Installation & Setup Guide

This guide provides step-by-step instructions for installing and configuring the SkypointShopifyPlugin from scratch. Any user can follow this guide to set up a fully functional Shopify-Skypoint shipping integration.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Initial Setup](#initial-setup)
3. [Shopify Partner Account Setup](#shopify-partner-account-setup)
4. [Application Configuration](#application-configuration)
5. [Running the Application](#running-the-application)
6. [Shopify App Installation](#shopify-app-installation)
7. [Skypoint Configuration](#skypoint-configuration)
8. [Testing the Integration](#testing-the-integration)
9. [Production Deployment](#production-deployment)
10. [Troubleshooting](#troubleshooting)

---

## Prerequisites

### Required Software

- **.NET 9.0 SDK** - Download from [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
- **Git** - For cloning the repository (optional)
- **Code Editor** - Visual Studio 2022, VS Code, or any preferred editor

### Required Accounts

- **Shopify Partner Account** - Create at [https://partners.shopify.com](https://partners.shopify.com)
- **Skypoint API Account** - Contact Skypoint for API credentials
- **Test Shopify Store** - For development and testing

### Optional Tools

- **ngrok** - For local development with HTTPS tunneling
- **Postman** - For API testing
- **PowerShell** - For running setup scripts

---

## Initial Setup

### Step 1: Obtain the Project Code

#### Option A: Clone from Git Repository
```bash
git clone <repository-url>
cd SkypointShopifyPlugin
```

#### Option B: Download and Extract
1. Download the project ZIP file
2. Extract to a directory (e.g., `C:\Projects\SkypointShopifyPlugin`)
3. Navigate to the project directory

### Step 2: Verify .NET Installation

Open a terminal/command prompt and run:

```bash
dotnet --version
```

Expected output: `9.0.xxx` or higher

If .NET is not installed, download and install from the official website.

### Step 3: Restore Dependencies

```bash
cd SkypointShopifyPlugin
dotnet restore
```

This will restore all NuGet packages required by the project.

### Step 4: Build the Solution

```bash
dotnet build
```

Expected output: Build succeeded with 0 warnings.

---

## Shopify Partner Account Setup

### Step 1: Create Shopify Partner Account

1. Go to [https://partners.shopify.com](https://partners.shopify.com)
2. Click "Sign up" or "Log in"
3. Complete the registration process
4. Verify your email address

### Step 2: Create a New App

1. In Shopify Partner Dashboard, click "Apps" → "Create app"
2. Choose "Custom app" (recommended for full control)
3. Fill in app details:
   - **App name**: Skypoint Shipping
   - **App URL**: Your development URL (e.g., `https://your-ngrok-url.ngrok-free.app`)
4. Click "Create app"

### Step 3: Configure App Scopes

In the app configuration, set the following scopes:

```
read_orders,write_orders,read_products,write_products,read_shipping,write_shipping,read_fulfillments,write_fulfillments
```

These scopes are required for:
- Reading and processing orders
- Reading product information
- Managing shipping settings
- Creating and managing fulfillments

### Step 4: Obtain API Credentials

1. In the app dashboard, go to "API credentials"
2. Copy the following values:
   - **API Key** (Client ID)
   - **API Secret Key** (Client Secret)
3. Save these securely - you'll need them for configuration

### Step 5: Configure App URLs

Set the following URLs in your Shopify app configuration:

- **App URL**: `https://your-domain.com`
- **Allowed redirection URL(s)**: `https://your-domain.com/api/shopify/auth`

**Note**: For local development, use ngrok to get a HTTPS URL.

---

## Application Configuration

### Step 1: Create Environment File

Navigate to the project root and create a `.env` file:

```bash
# Copy the example file
copy .env.example .env
```

### Step 2: Edit .env File

Open `.env` in your text editor and configure the following:

```env
# ============================================================
# SHOPIFY CONFIGURATION
# ============================================================

# Your Shopify App API Key (Client ID)
Shopify__ClientId=your_shopify_api_key_here

# Your Shopify App API Secret Key (Client Secret)
Shopify__ClientSecret=your_shopify_api_secret_here

# The URL where Shopify redirects after OAuth
# For local development with ngrok: https://xxxx.ngrok-free.app/api/shopify/auth
# For production: https://your-domain.com/api/shopify/auth
Shopify__RedirectUri=https://your-domain.com/api/shopify/auth

# Your Shopify Webhook Secret (from Shopify Partner Dashboard)
Shopify__WebhookSecret=your_webhook_secret_here

# ============================================================
# SKYPOINT API CONFIGURATION
# ============================================================

# Skypoint API Base URL (default: https://uat.skypoint.online)
SkypointApi__BaseUrl=https://uat.skypoint.online

# Optional: Override default endpoints if needed
# SkypointApi__LoginEndpoint=/api/service/session/customer/login
# SkypointApi__RegisterEndpoint=/api/service/session/customer/register
# SkypointApi__RateEndpoint=/api/service/rate/engine/quote
# SkypointApi__BookingEndpoint=/api/service/booking/create
# SkypointApi__TimeoutSeconds=30
# SkypointApi__MaxRetryAttempts=3
```

### Step 3: Obtain Webhook Secret

1. In Shopify Partner Dashboard, go to your app
2. Navigate to "Webhooks" section
3. The webhook secret will be displayed
4. Copy this to your `.env` file as `Shopify__WebhookSecret`

### Step 4: (Optional) Configure appsettings.json

You can also configure settings in `SkypointShopifyPlugin.WebAPI/appsettings.json`:

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
    "ClientId": "",
    "ClientSecret": "",
    "Scopes": "read_orders,write_orders,read_products,write_products,read_shipping,write_shipping,read_fulfillments,write_fulfillments",
    "RedirectUri": "",
    "WebhookSecret": ""
  }
}
```

**Note**: `.env` file values override `appsettings.json` values.

---

## Running the Application

### Option A: Development with Visual Studio

1. Open `SkypointShopifyPlugin.sln` in Visual Studio 2022
2. Set `SkypointShopifyPlugin.WebAPI` as startup project
3. Press F5 or click "Start" button
4. Application will start at `https://localhost:5001`

### Option B: Development with VS Code

1. Open the project folder in VS Code
2. Open terminal: `Ctrl + ``
3. Run the following command:

```bash
cd SkypointShopifyPlugin.WebAPI
dotnet run
```

4. Application will start at the configured port (default: 5001)

### Option C: Command Line

```bash
cd SkypointShopifyPlugin.WebAPI
dotnet run
```

### Option D: Local Development with ngrok

For local development, you need a public HTTPS URL for Shopify callbacks:

1. **Install ngrok** from [https://ngrok.com](https://ngrok.com)
2. Start ngrok:

```bash
ngrok http 5001
```

3. Copy the HTTPS URL (e.g., `https://xxxx.ngrok-free.app`)
4. Update your `.env` file:

```env
Shopify__RedirectUri=https://xxxx.ngrok-free.app/api/shopify/auth
```

5. Restart the application

---

## Shopify App Installation

### Step 1: Access the Install URL

Open your browser and navigate to:

```
https://your-domain.com/api/shopify?shop=your-store.myshopify.com
```

Replace:
- `your-domain.com` with your ngrok URL or production domain
- `your-store.myshopify.com` with your actual Shopify store domain

### Step 2: Complete OAuth Authorization

1. You'll be redirected to Shopify's authorization page
2. Review the requested permissions
3. Click "Install app" to authorize
4. You'll be redirected back to the application

### Step 3: Automatic Setup

After successful authorization, the application will automatically:
- Save your Shopify access token
- Register the carrier service for shipping rates
- Register webhooks for order events
- Redirect you to the dashboard

### Step 4: Verify Installation

1. Log in to your Shopify store admin
2. Go to "Settings" → "Apps and sales channels"
3. You should see "Skypoint Shipping" in the installed apps list
4. Click on the app to verify it's active

---

## Skypoint Configuration

### Step 1: Access the Dashboard

After installation, you'll be redirected to the dashboard. If not, navigate to:

```
https://your-domain.com/index.html?shop=your-store.myshopify.com
```

### Step 2: Create Skypoint Account

If you don't have a Skypoint account:

1. Click "Register" on the login page
2. Fill in the registration form:
   - Email address
   - Password
   - Company name
   - Contact information
3. Submit the form
4. Verify your email if required

### Step 3: Login to Skypoint

1. Enter your Skypoint email and password
2. Click "Login"
3. If successful, you'll be redirected to the dashboard

### Step 4: Configure Default Settings

In the dashboard, configure the following:

#### Parcel Dimensions
- **Default Parcel Length**: 30 cm
- **Default Parcel Breadth**: 30 cm
- **Default Parcel Height**: 23 cm
- **Default Parcel Mass**: 5 kg
- **Default Parcel Type**: A4_Text_Book

#### Postal Code Mappings (Optional)
If you need to map specific postal codes to regions, configure mappings in the configuration section.

#### Suburb Mappings (Optional)
Map suburbs to recognized Skypoint suburbs for accurate routing.

### Step 5: Save Configuration

Click "Save Configuration" to store your settings. These will be used for all shipping calculations.

---

## Testing the Integration

### Test 1: Carrier Service (Shipping Rates)

1. Go to your Shopify store
2. Add a product to cart
3. Proceed to checkout
4. Enter shipping address
5. At shipping method selection, you should see:
   - Skypoint shipping options
   - Real-time rates based on destination
6. Verify rates are reasonable and accurate

### Test 2: Order Creation

1. Complete a test order in Shopify
2. Select Skypoint as shipping method
3. Complete payment
4. Check application logs for order processing
5. Verify booking was created in Skypoint system

### Test 3: Webhook Processing

1. In Shopify Partner Dashboard, go to your app
2. Navigate to "Webhooks"
3. Find the webhooks registered by the app:
   - `orders/create`
   - `orders/updated`
   - `orders/cancelled`
   - `app/uninstalled`
4. Click "Send test webhook" next to each webhook
5. Verify application receives and processes the webhook

### Test 4: Order Tracking

1. After creating a test order
2. Check the dashboard for order status
3. Verify tracking number is displayed
4. Click tracking link to verify it works

---

## Production Deployment

### Option A: Azure App Service (Recommended)

#### Prerequisites
- Azure account (free tier available)
- Azure CLI or Azure Portal access

#### Deployment Steps

1. **Publish the Application**

```bash
cd SkypointShopifyPlugin.WebAPI
dotnet publish -c Release -o ./publish
```

2. **Create Resource Group**

```bash
az group create --name SkypointShopifyRG --location eastus
```

3. **Create App Service Plan**

```bash
az appservice plan create --name SkypointShopifyPlan --resource-group SkypointShopifyRG --sku B1 --is-linux
```

4. **Create Web App**

```bash
az webapp create --name SkypointShopifyApp --resource-group SkypointShopifyRG --plan SkypointShopifyPlan --runtime "DOTNET|9.0"
```

5. **Deploy Application**

```bash
cd publish
az webapp deployment source config-zip --resource-group SkypointShopifyRG --name SkypointShopifyApp --src ../publish.zip
```

6. **Configure Environment Variables**

In Azure Portal:
- Go to your Web App
- Navigate to "Configuration" → "Environment variables"
- Add all settings from your `.env` file
- Save configuration

7. **Configure Custom Domain** (Optional)

- Purchase domain from registrar
- Add custom domain in Azure Portal
- Configure DNS records
- SSL certificate is automatic

### Option B: Docker Deployment

We provide containerized setup for both the **WebAPI** and **WebUI** services using Docker Compose.

#### Dockerfiles

There are two separate Dockerfiles:
1. [Dockerfile](file:///d:/New%20folder/SkypointShopifyPlugin/SkypointShopifyPlugin.WebAPI/Dockerfile) for the backend API.
2. [Dockerfile](file:///d:/New%20folder/SkypointShopifyPlugin/SkypointShopifyPlugin.WebUI/Dockerfile) for the Blazor WebUI frontend.

#### Docker Compose Configuration

The root [docker-compose.yml](file:///d:/New%20folder/SkypointShopifyPlugin/docker-compose.yml) orchestrates these services:

```yaml
services:
  webapi:
    image: skypoint-shopify-plugin-api:latest
    build:
      context: .
      dockerfile: SkypointShopifyPlugin.WebAPI/Dockerfile
    ports:
      - "5126:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    env_file:
      - .env
    volumes:
      - ./SkypointShopifyPlugin.WebAPI/data:/app/data

  webui:
    image: skypoint-shopify-plugin-ui:latest
    build:
      context: .
      dockerfile: SkypointShopifyPlugin.WebUI/Dockerfile
    ports:
      - "5226:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - BackendApi__BaseUrl=http://webapi:8080
    env_file:
      - .env
    depends_on:
      - webapi
```

#### Build and Run with Docker Compose

1. **Configure Environment Variables**: Ensure your `.env` file at the root contains correct Shopify and Skypoint API credentials.
2. **Build and Start Containers**: Run the following command from the root directory:

```bash
docker compose up -d --build
```

This will build the images, launch both the WebAPI (accessible at `http://localhost:5126`) and WebUI (accessible at `http://localhost:5226`), and set up the `webapi-data` persistent volume for configurations and tokens.

3. **Stop Containers**:

```bash
docker compose down
```

### Option C: VPS/Cloud Server Deployment

#### Prerequisites
- Linux or Windows VPS
- .NET 9.0 Runtime installed
- Nginx or Apache (for reverse proxy)
- SSL certificate (Let's Encrypt recommended)

#### Deployment Steps

1. **Install .NET Runtime**

```bash
# Ubuntu/Debian
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-9.0
```

2. **Publish Application**

```bash
dotnet publish -c Release -o /var/www/skypoint-plugin
```

3. **Create Systemd Service**

Create `/etc/systemd/system/skypoint-plugin.service`:

```ini
[Unit]
Description=Skypoint Shopify Plugin
After=network.target

[Service]
Type=notify
WorkingDirectory=/var/www/skypoint-plugin
ExecStart=/usr/bin/dotnet /var/www/skypoint-plugin/SkypointShopifyPlugin.WebAPI.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
EnvironmentFile=/var/www/skypoint-plugin/.env

[Install]
WantedBy=multi-user.target
```

4. **Enable and Start Service**

```bash
sudo systemctl enable skypoint-plugin
sudo systemctl start skypoint-plugin
sudo systemctl status skypoint-plugin
```

5. **Configure Nginx Reverse Proxy**

Create `/etc/nginx/sites-available/skypoint-plugin`:

```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

6. **Enable SSL with Let's Encrypt**

```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com
```

---

## Troubleshooting

### Issue: Application Won't Start

**Symptoms**: Error when running `dotnet run`

**Solutions**:
1. Verify .NET 9.0 SDK is installed: `dotnet --version`
2. Run `dotnet restore` to restore dependencies
3. Check for port conflicts (default: 5001)
4. Verify `.env` file exists and is properly formatted

### Issue: Shopify OAuth Fails

**Symptoms**: Redirect loop or authorization error

**Solutions**:
1. Verify `Shopify__ClientId` and `Shopify__ClientSecret` are correct
2. Ensure `Shopify__RedirectUri` matches exactly in Shopify Partner Dashboard
3. Check that the redirect URI uses HTTPS (required by Shopify)
4. Verify app scopes are correctly configured in Shopify

### Issue: Carrier Service Not Showing Rates

**Symptoms**: No Skypoint rates at checkout

**Solutions**:
1. Verify carrier service is registered: Check application logs
2. Ensure callback URL uses HTTPS
3. Check that shop domain is correct in carrier service URL
4. Re-run carrier service registration via `/api/shopify/setup`
5. Verify Skypoint API credentials are valid

### Issue: Webhooks Not Received

**Symptoms**: Orders not processing automatically

**Solutions**:
1. Verify webhooks are registered in Shopify Partner Dashboard
2. Check webhook URLs are accessible from internet
3. Verify `Shopify__WebhookSecret` matches in Shopify Dashboard
4. Check application logs for webhook processing errors
5. Test webhooks manually from Shopify Dashboard

### Issue: Skypoint API Authentication Fails

**Symptoms**: Booking creation fails with 401/403 error

**Solutions**:
1. Verify Skypoint credentials in dashboard
2. Check Skypoint API base URL is correct
3. Re-authenticate to get fresh token
4. Verify Skypoint API is accessible from your server
5. Check Skypoint API status page for outages

### Issue: Invalid Postal Code Error

**Symptoms**: Booking rejected with postal code error

**Solutions**:
1. Verify postal code is valid for the region
2. Configure postal code mappings in dashboard
3. Check suburb mappings are correct
4. Ensure address data is complete in Shopify order

### Issue: Port Already in Use

**Symptoms**: Error "Address already in use"

**Solutions**:
1. Change port in `Properties/launchSettings.json`
2. Or kill the process using the port:
   ```bash
   # Windows
   netstat -ano | findstr :5001
   taskkill /PID <PID> /F
   
   # Linux/Mac
   lsof -ti:5001 | xargs kill -9
   ```

### Issue: ngrok URL Changes

**Symptoms**: App stops working after ngrok restart

**Solutions**:
1. Update `Shopify__RedirectUri` in `.env` with new ngrok URL
2. Re-run carrier service registration: `/api/shopify/setup`
3. Re-register webhooks if needed

---

## Configuration Reference

### Environment Variables

| Variable | Required | Description | Example |
|----------|----------|-------------|---------|
| `Shopify__ClientId` | Yes | Shopify API Key | `abc123...` |
| `Shopify__ClientSecret` | Yes | Shopify API Secret | `xyz789...` |
| `Shopify__RedirectUri` | Yes | OAuth callback URL | `https://domain.com/api/shopify/auth` |
| `Shopify__WebhookSecret` | Yes | Webhook signature secret | `secret123...` |
| `SkypointApi__BaseUrl` | No | Skypoint API base URL | `https://uat.skypoint.online` |
| `SkypointApi__LoginEndpoint` | No | Login endpoint path | `/api/service/session/customer/login` |
| `SkypointApi__RateEndpoint` | No | Rate endpoint path | `/api/service/rate/engine/quote` |
| `SkypointApi__BookingEndpoint` | No | Booking endpoint path | `/api/service/booking/create` |
| `SkypointApi__TimeoutSeconds` | No | API timeout in seconds | `30` |
| `SkypointApi__MaxRetryAttempts` | No | Max retry attempts | `3` |

### Default Parcel Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Length | 30 cm | Default parcel length |
| Breadth | 30 cm | Default parcel width |
| Height | 23 cm | Default parcel height |
| Mass | 5 kg | Default parcel weight |
| Type | A4_Text_Book | Default parcel type |

---

## Security Best Practices

### 1. Protect Sensitive Data

- Never commit `.env` file to version control
- Use environment variables in production
- Rotate secrets periodically
- Use strong, unique passwords

### 2. HTTPS Required

- Shopify requires HTTPS for all endpoints
- Use valid SSL certificates in production
- For development, use ngrok with HTTPS
- Never use HTTP in production

### 3. Webhook Security

- Always verify webhook signatures
- Keep webhook secret secure
- Implement rate limiting
- Validate all incoming data

### 4. Access Control

- Restrict admin dashboard access
- Implement authentication for admin endpoints
- Use API keys for external access
- Monitor access logs

### 5. Data Protection

- Encrypt tokens at rest
- Use secure token storage
- Implement proper error handling
- Log security events

---

## Monitoring and Maintenance

### Application Logs

Logs are written to:
- Console (development)
- File (production, if configured)

Monitor logs for:
- Error messages
- Failed webhook processing
- API authentication failures
- Order processing errors

### Health Checks

Implement health check endpoints:
- `/health` - Application health
- `/health/ready` - Readiness probe
- `/health/live` - Liveness probe

### Performance Monitoring

Monitor:
- Response times
- Error rates
- Order processing success rate
- API call success rate

### Backup Strategy

Backup the following:
- `.env` file (secure location)
- `data/` directory (tokens and configurations)
- Application logs
- Configuration files

---

## Support and Resources

### Documentation

- **README.md** - Project overview and quick start
- **IMPLEMENTATION_PLAN.md** - Detailed implementation guide
- **SETUP_GUIDE.md** - This document

### Shopify Resources

- [Shopify Partner Documentation](https://partners.shopify.com/docs)
- [Shopify API Reference](https://shopify.dev/api)
- [Shopify Webhooks Guide](https://shopify.dev/docs/api/admin/rest/webhooks)

### Skypoint Resources

- Contact Skypoint for API documentation
- API support contact information
- API status page (if available)

### Community

- GitHub Issues (if public repository)
- Stack Overflow (tag: shopify, dotnet)
- Shopify Community Forums

---

## Next Steps

After completing setup:

1. **Test thoroughly** with test orders
2. **Monitor logs** for any issues
3. **Configure alerts** for critical errors
4. **Set up backups** for configuration data
5. **Document custom configurations** for your team
6. **Train users** on dashboard usage
7. **Plan for scaling** if high volume expected

---

## Checklist

Use this checklist to verify your setup:

- [ ] .NET 9.0 SDK installed
- [ ] Project cloned/downloaded
- [ ] Dependencies restored (`dotnet restore`)
- [ ] Solution builds successfully (`dotnet build`)
- [ ] Shopify Partner account created
- [ ] Shopify app created
- [ ] API credentials obtained
- [ ] `.env` file configured
- [ ] Application runs locally
- [ ] ngrok set up (for development)
- [ ] Shopify app installed on test store
- [ ] OAuth flow completed
- [ ] Carrier service registered
- [ ] Webhooks registered
- [ ] Skypoint account configured
- [ ] Dashboard accessible
- [ ] Test order created
- [ ] Shipping rates displayed
- [ ] Booking created in Skypoint
- [ ] Webhooks processing correctly
- [ ] Production deployment completed
- [ ] SSL certificate configured
- [ ] Monitoring configured
- [ ] Backup strategy in place

---

**Document Version**: 1.0  
**Last Updated**: 2025-06-30  
**For**: SkypointShopifyPlugin v1.0  
**Status**: Production Ready
