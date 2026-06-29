# Script to remove unwanted stores from credential store
# This will remove all stores except the one you want to keep

$StoreToKeep = "suresh-dev-store-2.myshopify.com"

Write-Host "Cleaning up credential store..." -ForegroundColor Cyan
Write-Host "Keeping only: $StoreToKeep" -ForegroundColor Green
Write-Host ""

# Read the credential store file
$credFile = "SkypointShopifyPlugin.WebAPI\data\skypoint_credentials.json"
if (Test-Path $credFile) {
    $creds = Get-Content $credFile | ConvertFrom-Json
    
    $stores = $creds | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name
    Write-Host "Current stores in credential store:" -ForegroundColor Yellow
    foreach ($store in $stores) {
        Write-Host "  - $store" -ForegroundColor Gray
    }
    Write-Host ""
    
    # Remove unwanted stores
    $removed = 0
    foreach ($store in $stores) {
        if ($store -ne $StoreToKeep) {
            Write-Host "Removing: $store" -ForegroundColor Red
            $creds.PSObject.Properties.Remove($store)
            $removed++
        }
    }
    
    if ($removed -gt 0) {
        # Save the cleaned credential store
        $creds | ConvertTo-Json -Depth 10 | Set-Content $credFile
        Write-Host ""
        Write-Host "Removed $removed unwanted store(s)" -ForegroundColor Green
    } else {
        Write-Host "No stores to remove" -ForegroundColor Yellow
    }
} else {
    Write-Host "Credential store file not found: $credFile" -ForegroundColor Red
}

# Also clean up Shopify tokens
$tokenFile = "SkypointShopifyPlugin.WebAPI\data\shopify_tokens.json"
if (Test-Path $tokenFile) {
    Write-Host ""
    Write-Host "Cleaning up Shopify tokens..." -ForegroundColor Cyan
    $tokens = Get-Content $tokenFile | ConvertFrom-Json
    
    $stores = $tokens | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name
    Write-Host "Current stores in token store:" -ForegroundColor Yellow
    foreach ($store in $stores) {
        Write-Host "  - $store" -ForegroundColor Gray
    }
    Write-Host ""
    
    $removed = 0
    foreach ($store in $stores) {
        if ($store -ne $StoreToKeep) {
            Write-Host "Removing: $store" -ForegroundColor Red
            $tokens.PSObject.Properties.Remove($store)
            $removed++
        }
    }
    
    if ($removed -gt 0) {
        $tokens | ConvertTo-Json -Depth 10 | Set-Content $tokenFile
        Write-Host ""
        Write-Host "Removed $removed unwanted token(s)" -ForegroundColor Green
    } else {
        Write-Host "No tokens to remove" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Cleanup complete!" -ForegroundColor Green
Write-Host "Restart the application to apply changes." -ForegroundColor Cyan
