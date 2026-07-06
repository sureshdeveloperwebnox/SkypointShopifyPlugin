# Shopify Webhook Registration Script
# This script uses the built-in webhook sync endpoint to register webhooks

# Configuration & Centralized .env Parsing
$ScriptDir = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
$EnvPath = Join-Path $ScriptDir ".env"

$ShopDomain = "deeprintztestapp.myshopify.com"  # Change to your store domain
$PublicBaseUrl = ""
$ApiBaseUrl = "http://localhost:5126"  # Fallback local WebAPI port

if (Test-Path $EnvPath) {
    Write-Host "Loading configuration from centralized .env file..." -ForegroundColor Cyan
    Get-Content $EnvPath | Where-Object { $_ -match '=' -and $_ -notmatch '^#' } | ForEach-Object {
        $parts = $_ -split '=', 2
        $key = $parts[0].Trim()
        $val = $parts[1].Trim()
        if ($key -eq "Shopify__RedirectUri") {
            # Extract base URL: e.g. https://87c5-223-185-24-226.ngrok-free.app/api/shopify/auth -> https://87c5-223-185-24-226.ngrok-free.app
            if ($val -match '^(https?://[^/]+)') {
                $PublicBaseUrl = $Matches[1]
            }
        }
        elseif ($key -eq "BackendApi__BaseUrl") {
            $ApiBaseUrl = $val
        }
    }
}

if (-not $PublicBaseUrl) {
    Write-Error "Error: Could not find or parse Shopify__RedirectUri in .env"
    exit 1
}

# Automatically sync .env configuration to shopify.app.toml files
Write-Host "Syncing .env configuration to shopify.app.toml files..." -ForegroundColor Cyan
$TomlPaths = @(
    (Join-Path $ScriptDir "shopify.app.toml"),
    (Join-Path $ScriptDir "SkypointShopifyPlugin.WebAPI\shopify.app.toml")
)

foreach ($path in $TomlPaths) {
    if (Test-Path $path) {
        Write-Host "Updating $path with public URL: $PublicBaseUrl..." -ForegroundColor Yellow
        $content = Get-Content $path
        # Match any ngrok-free URL pattern and replace it
        $updatedContent = $content | ForEach-Object {
            if ($_ -match '(https?://[a-zA-Z0-9\-]+\.ngrok-free\.(app|dev|io))([^"\s]*)') {
                $oldUrl = $Matches[0]
                $pathPart = $Matches[3]
                $newUrl = "$PublicBaseUrl$pathPart"
                $_ -replace [regex]::Escape($oldUrl), $newUrl
            } else {
                $_
            }
        }
        $updatedContent | Set-Content $path
    }
}

Write-Host "Syncing webhooks for shop: $ShopDomain" -ForegroundColor Cyan
Write-Host "Base URL: $PublicBaseUrl" -ForegroundColor Cyan
Write-Host "API URL: $ApiBaseUrl" -ForegroundColor Cyan
Write-Host ""

# Prepare request body
$body = @{
    shopDomain = $ShopDomain
    publicBaseUrl = $PublicBaseUrl
} | ConvertTo-Json

$headers = @{
    "Content-Type" = "application/json"
}

# Call the sync webhook endpoint
Write-Host "Calling webhook sync endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$ApiBaseUrl/api/shopify/sync-webhooks" -Method Post -Headers $headers -Body $body
    Write-Host "Success: $($response.message)" -ForegroundColor Green
}
catch {
    $errorDetails = $_.ErrorDetails.Message
    if ($errorDetails) {
        $errorJson = $errorDetails | ConvertFrom-Json
        Write-Host "Error: $($errorJson.message)" -ForegroundColor Red
    }
    else {
        Write-Host "Error: $_" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Make sure your application is running on $ApiBaseUrl" -ForegroundColor Yellow
    Write-Host "And that the app is installed for $ShopDomain" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Webhook sync complete!" -ForegroundColor Green
Write-Host "You can verify webhooks in Shopify Admin → Settings → Notifications → Webhooks" -ForegroundColor Cyan
