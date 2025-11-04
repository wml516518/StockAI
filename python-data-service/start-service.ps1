# PowerShell 启动脚本
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "启动股票数据服务 (Python + AKShare)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查Python是否安装
$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCmd) {
    Write-Host "[错误] 未检测到Python，请先安装Python 3.8+" -ForegroundColor Red
    Write-Host "下载地址: https://www.python.org/downloads/" -ForegroundColor Yellow
    Read-Host "按Enter键退出"
    exit 1
}

Write-Host "[1/3] 检查依赖..." -ForegroundColor Yellow
$flaskInstalled = python -c "import flask" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "[2/3] 安装依赖包..." -ForegroundColor Yellow
    pip install -r requirements.txt
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[错误] 依赖安装失败，请检查网络连接" -ForegroundColor Red
        Read-Host "按Enter键退出"
        exit 1
    }
} else {
    Write-Host "[2/3] 依赖已安装" -ForegroundColor Green
}

Write-Host "[3/3] 启动服务..." -ForegroundColor Yellow
Write-Host ""
Write-Host "服务将在 http://localhost:5001 启动" -ForegroundColor Green
Write-Host "按 Ctrl+C 停止服务" -ForegroundColor Yellow
Write-Host ""

# 切换到脚本所在目录
Set-Location $PSScriptRoot

# 启动服务
python stock_data_service.py

# 如果服务退出，显示提示
Write-Host ""
Write-Host "服务已停止" -ForegroundColor Yellow
Read-Host "按Enter键退出"

