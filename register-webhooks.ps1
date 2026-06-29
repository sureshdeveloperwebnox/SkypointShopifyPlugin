# Shopify Webhook Registration Script
# This script uses the built-in webhook sync endpoint to register webhooks

# Configuration
$ShopDomain = "suresh-dev-store-2.myshopify.com"  # Change to your store domain
$PublicBaseUrl = "https://eboni-unprizable-discriminatingly.ngrok-free.dev"  # Your ngrok URL
$ApiBaseUrl = "http://localhost:5000"  # Your local API URL

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
