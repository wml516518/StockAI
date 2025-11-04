# 停止所有服务
# PowerShell版本

$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  停止所有服务" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "正在停止服务..." -ForegroundColor Yellow
Write-Host ""

# 停止Python服务
Write-Host "[1/3] 停止Python数据服务..." -ForegroundColor Cyan
$pythonProcesses = Get-Process python -ErrorAction SilentlyContinue
if ($pythonProcesses) {
    # 尝试通过窗口标题停止
    Get-Process | Where-Object { $_.MainWindowTitle -like "*Python数据服务*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    # 停止所有Python进程（谨慎使用，可能会停止其他Python程序）
    # $pythonProcesses | Stop-Process -Force
    Write-Host "  ✅ Python服务已停止" -ForegroundColor Green
} else {
    Write-Host "  ⚠️  未找到Python服务进程" -ForegroundColor Yellow
}
Write-Host ""

# 停止后端API服务
Write-Host "[2/3] 停止后端API服务..." -ForegroundColor Cyan
Get-Process | Where-Object { $_.MainWindowTitle -like "*后端API服务*" } | Stop-Process -Force -ErrorAction SilentlyContinue
$dotnetProcesses = Get-Process dotnet -ErrorAction SilentlyContinue
if ($dotnetProcesses) {
    # 注意：这会停止所有dotnet进程，建议手动停止
    Write-Host "  ⚠️  检测到dotnet进程，请手动检查" -ForegroundColor Yellow
    Write-Host "  💡 提示：可以通过任务管理器关闭对应窗口" -ForegroundColor Gray
} else {
    Write-Host "  ✅ 后端API服务已停止" -ForegroundColor Green
}
Write-Host ""

# 停止前端服务
Write-Host "[3/3] 停止前端开发服务器..." -ForegroundColor Cyan
Get-Process | Where-Object { $_.MainWindowTitle -like "*前端开发服务器*" } | Stop-Process -Force -ErrorAction SilentlyContinue
$nodeProcesses = Get-Process node -ErrorAction SilentlyContinue
if ($nodeProcesses) {
    # 注意：这会停止所有node进程，建议手动停止
    Write-Host "  ⚠️  检测到node进程，请手动检查" -ForegroundColor Yellow
    Write-Host "  💡 提示：可以通过任务管理器关闭对应窗口" -ForegroundColor Gray
} else {
    Write-Host "  ✅ 前端服务已停止" -ForegroundColor Green
}
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  所有服务已停止" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Read-Host "按Enter键退出"

