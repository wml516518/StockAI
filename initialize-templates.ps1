# Initialize Optimized Stock Screening Templates
# Usage: .\initialize-templates.ps1

$apiUrl = "http://localhost:5000/api/ScreenTemplate/initialize-optimized"

Write-Host "Initializing optimized templates..." -ForegroundColor Yellow
Write-Host "API URL: $apiUrl" -ForegroundColor Gray

try {
    $response = Invoke-RestMethod -Uri $apiUrl -Method POST -ContentType "application/json"
    
    Write-Host ""
    Write-Host "[SUCCESS] Templates initialized successfully!" -ForegroundColor Green
    Write-Host "  - Updated: $($response.updated) templates" -ForegroundColor Cyan
    Write-Host "  - Created: $($response.created) templates" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Message: $($response.message)" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "[ERROR] Request failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode.value__
        Write-Host "HTTP Status Code: $statusCode" -ForegroundColor Red
        
        if ($statusCode -eq 404) {
            Write-Host ""
            Write-Host "Tip: Please ensure API service is running (http://localhost:5000)" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host ""
        Write-Host "Tips:" -ForegroundColor Yellow
        Write-Host "  1. API service is running" -ForegroundColor Yellow
        Write-Host "  2. Port number is correct (default: 5000)" -ForegroundColor Yellow
        Write-Host "  3. Network connection is normal" -ForegroundColor Yellow
    }
    
    exit 1
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green

