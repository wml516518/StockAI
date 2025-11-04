# 刷新环境变量脚本
# 在新打开的 PowerShell 窗口中运行此脚本，或添加到 PowerShell 配置文件

Write-Host "正在刷新环境变量..." -ForegroundColor Yellow

# 刷新 PATH 环境变量
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

# 验证 Node.js 和 npm
Write-Host "`n验证安装:" -ForegroundColor Cyan
try {
    $nodeVersion = node --version
    $npmVersion = npm --version
    Write-Host "✅ Node.js: $nodeVersion" -ForegroundColor Green
    Write-Host "✅ npm: $npmVersion" -ForegroundColor Green
    Write-Host "`n环境变量已刷新！现在可以使用 npm 命令了。" -ForegroundColor Green
} catch {
    Write-Host "❌ Node.js 或 npm 未找到。请确保已安装 Node.js。" -ForegroundColor Red
    Write-Host "安装方法: winget install OpenJS.NodeJS.LTS" -ForegroundColor Yellow
}

