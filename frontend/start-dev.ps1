# 启动前端开发服务器 (PowerShell版本)
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  启动前端开发服务器" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 刷新环境变量以识别Node.js
$systemPath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
$userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
$env:Path = "$systemPath;$userPath"

# 检查Node.js
$nodeFound = $false
if (Test-Path "C:\Program Files\nodejs\node.exe") {
    $env:Path += ";C:\Program Files\nodejs"
    $nodeFound = $true
} elseif (Test-Path "${env:ProgramFiles(x86)}\nodejs\node.exe") {
    $env:Path += ";${env:ProgramFiles(x86)}\nodejs"
    $nodeFound = $true
}

if (-not $nodeFound) {
    $nodePath = Get-Command node -ErrorAction SilentlyContinue
    if ($nodePath) {
        $nodeFound = $true
    }
}

if (-not $nodeFound) {
    Write-Host "[错误] 未检测到Node.js" -ForegroundColor Red
    Write-Host ""
    Write-Host "[解决方案]" -ForegroundColor Yellow
    Write-Host "[步骤1] 确保已安装Node.js: https://nodejs.org/" -ForegroundColor Yellow
    Write-Host "[步骤2] 刷新环境变量，运行以下命令" -ForegroundColor Yellow
    Write-Host '    $env:Path += ";C:\Program Files\nodejs"' -ForegroundColor Gray
    Write-Host "[步骤3] 或重新打开PowerShell窗口" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "按Enter键退出"
    exit 1
}

Write-Host "[1/2] 检查环境..." -ForegroundColor Green
node --version
npm --version
Write-Host ""

Write-Host "[2/2] 启动开发服务器..." -ForegroundColor Green
Write-Host ""

# 检查依赖
if (-not (Test-Path "node_modules")) {
    Write-Host "检测到缺少依赖，正在安装..." -ForegroundColor Yellow
    npm install
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[错误] 依赖安装失败" -ForegroundColor Red
        Read-Host "按Enter键退出"
        exit 1
    }
    Write-Host ""
}

Write-Host "服务将在 http://localhost:5173 启动" -ForegroundColor Cyan
Write-Host "按 Ctrl+C 停止服务" -ForegroundColor Yellow
Write-Host ""
npm run dev

Read-Host "按Enter键退出"

