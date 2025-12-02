# 测试东方财富历史数据接口
$stockCode = "000001"
$market = if ($stockCode.StartsWith("6")) { "1" } else { "0" }
$secid = "$market.$stockCode"
$endDate = Get-Date
$startDate = $endDate.AddDays(-30)
$beg = $startDate.ToString("yyyyMMdd")
$end = $endDate.ToString("yyyyMMdd")

$url = "http://push2his.eastmoney.com/api/qt/stock/kline/get?secid=$secid&fields1=f1,f2,f3,f4&fields2=f51,f52,f53,f54,f55,f56,f57&klt=101&fqt=1&beg=$beg&end=$end"

Write-Host "测试URL: $url" -ForegroundColor Cyan
Write-Host "股票代码: $stockCode" -ForegroundColor Cyan
Write-Host "日期范围: $($startDate.ToString('yyyy-MM-dd')) 到 $($endDate.ToString('yyyy-MM-dd'))" -ForegroundColor Cyan
Write-Host ""

# 测试重试机制
$maxRetries = 3
$attempts = @()

for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
    $attemptInfo = @{
        attemptNumber = $attempt
        timestamp = Get-Date
        success = $false
        error = $null
        statusCode = $null
        durationMs = $null
    }
    
    try {
        $startTime = Get-Date
        $response = Invoke-WebRequest -Uri $url -Method GET -Headers @{
            "User-Agent" = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
            "Referer" = "http://quote.eastmoney.com/"
        } -TimeoutSec 30 -ErrorAction Stop
        
        $duration = ((Get-Date) - $startTime).TotalMilliseconds
        $attemptInfo.success = $true
        $attemptInfo.durationMs = [math]::Round($duration, 2)
        $attemptInfo.statusCode = $response.StatusCode
        
        $attempts += $attemptInfo
        
        Write-Host "✅ 尝试 $attempt/$maxRetries 成功！" -ForegroundColor Green
        Write-Host "   状态码: $($response.StatusCode)" -ForegroundColor Green
        Write-Host "   耗时: $([math]::Round($duration, 2)) 毫秒" -ForegroundColor Green
        Write-Host "   响应长度: $($response.Content.Length) 字符" -ForegroundColor Green
        Write-Host ""
        
        # 解析JSON
        try {
            $jsonData = $response.Content | ConvertFrom-Json
            if ($jsonData.data.klines) {
                $totalRecords = ($jsonData.data.klines | Measure-Object).Count
                Write-Host "📊 数据解析成功！" -ForegroundColor Green
                Write-Host "   总记录数: $totalRecords" -ForegroundColor Green
                Write-Host ""
                
                # 显示前5条数据
                Write-Host "前5条数据示例:" -ForegroundColor Yellow
                $count = 0
                foreach ($k in $jsonData.data.klines) {
                    if ($count -ge 5) { break }
                    $parts = $k.ToString().Split(',')
                    if ($parts.Length -ge 7) {
                        Write-Host "   [$($count + 1)] 日期: $($parts[0]), 开盘: $($parts[1]), 收盘: $($parts[2]), 最高: $($parts[3]), 最低: $($parts[4]), 成交量: $($parts[5]), 成交额: $($parts[6])" -ForegroundColor White
                    }
                    $count++
                }
            }
            else {
                Write-Host "⚠️ 响应中没有klines数据" -ForegroundColor Yellow
                $contentPreview = $response.Content.Substring(0, [Math]::Min(500, $response.Content.Length))
                Write-Host "   原始响应: $contentPreview" -ForegroundColor Yellow
            }
        }
        catch {
            Write-Host "⚠️ JSON解析失败: $($_.Exception.Message)" -ForegroundColor Yellow
            $contentPreview = $response.Content.Substring(0, [Math]::Min(500, $response.Content.Length))
            Write-Host "   原始响应前500字符: $contentPreview" -ForegroundColor Yellow
        }
        
        break
    }
    catch {
        $statusCode = 0
        if ($_.Exception.Message -match "502" -or $_.Exception.Message -match "Bad Gateway") {
            $statusCode = 502
        }
        elseif ($_.Exception.Message -match "500") {
            $statusCode = 500
        }
        elseif ($_.Exception.Message -match "503") {
            $statusCode = 503
        }
        
        $attemptInfo.error = $_.Exception.Message
        if ($statusCode -gt 0) {
            $attemptInfo.statusCode = $statusCode
        }
        $attempts += $attemptInfo
        
        Write-Host "❌ 尝试 $attempt/$maxRetries 失败" -ForegroundColor Red
        Write-Host "   错误: $($_.Exception.Message)" -ForegroundColor Red
        if ($statusCode -gt 0) {
            Write-Host "   状态码: $statusCode" -ForegroundColor Red
        }
        
        if ($attempt -lt $maxRetries) {
            $delay = $attempt * 2
            Write-Host "   等待 $delay 秒后重试..." -ForegroundColor Yellow
            Start-Sleep -Seconds $delay
        }
        else {
            Write-Host "   所有重试均失败" -ForegroundColor Red
        }
        Write-Host ""
    }
}

Write-Host "`n📋 重试统计:" -ForegroundColor Cyan
$attempts | Format-Table -AutoSize
