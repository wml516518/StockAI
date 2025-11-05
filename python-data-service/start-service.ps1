# PowerShell 启动脚本
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "启动股票数据服务 (Python + AKShare)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查Python是否安装（尝试多种方式）
$pythonExe = $null
$pythonCommands = @("python", "py", "python3")

# 首先尝试命令检测
foreach ($cmd in $pythonCommands) {
    $pythonCmd = Get-Command $cmd -ErrorAction SilentlyContinue
    if ($pythonCmd) {
        # 验证Python版本
        try {
            $version = & $cmd --version 2>&1
            if ($version -match "Python (\d+)\.(\d+)") {
                $major = [int]$matches[1]
                $minor = [int]$matches[2]
                if ($major -ge 3 -and $minor -ge 8) {
                    $pythonExe = $cmd
                    Write-Host "检测到Python: $version" -ForegroundColor Green
                    break
                }
            }
        } catch {
            continue
        }
    }
}

# 如果命令检测失败，尝试常见安装路径
if (-not $pythonExe) {
    $commonPaths = @(
        "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python311\python.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python310\python.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python39\python.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python38\python.exe",
        "C:\Python312\python.exe",
        "C:\Python311\python.exe",
        "C:\Python310\python.exe",
        "C:\Python39\python.exe",
        "C:\Python38\python.exe",
        "C:\Program Files\Python312\python.exe",
        "C:\Program Files\Python311\python.exe",
        "C:\Program Files\Python310\python.exe"
    )
    
    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            try {
                $version = & $path --version 2>&1
                if ($version -match "Python (\d+)\.(\d+)") {
                    $major = [int]$matches[1]
                    $minor = [int]$matches[2]
                    if ($major -ge 3 -and $minor -ge 8) {
                        $pythonExe = $path
                        Write-Host "从路径检测到Python: $version" -ForegroundColor Green
                        break
                    }
                }
            } catch {
                continue
            }
        }
    }
}

if (-not $pythonExe) {
    Write-Host "[错误] 未检测到Python 3.8+，请先安装Python" -ForegroundColor Red
    Write-Host "下载地址: https://www.python.org/downloads/" -ForegroundColor Yellow
    Write-Host "提示: 安装Python时请勾选 'Add Python to PATH' 选项" -ForegroundColor Gray
    Read-Host "按Enter键退出"
    exit 1
}

Write-Host "[1/3] 检查依赖..." -ForegroundColor Yellow
# 使用检测到的Python可执行文件检查依赖
$flaskInstalled = & $pythonExe -c "import flask" 2>$null

if ($LASTEXITCODE -ne 0) {
    Write-Host "[2/3] 安装依赖包..." -ForegroundColor Yellow
    & $pythonExe -m pip install -r requirements.txt
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

# 启动服务（使用检测到的Python可执行文件）
& $pythonExe stock_data_service.py

# 如果服务退出，显示提示
Write-Host ""
Write-Host "服务已停止" -ForegroundColor Yellow
Read-Host "按Enter键退出"

