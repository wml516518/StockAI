# 股票分析系统 - 一键启动所有服务
# PowerShell版本

$ErrorActionPreference = "Continue"

function Write-Step {
    param([string]$Message, [string]$Color = "Cyan")
    Write-Host $Message -ForegroundColor $Color
}

function Write-Success {
    param([string]$Message)
    Write-Host "  ✅ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "  ⚠️  $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "  ❌ $Message" -ForegroundColor Red
}

# 刷新环境变量
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  股票分析系统 - 一键启动所有服务" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查是否在正确的目录
if (-not (Test-Path "src\StockAnalyse.Api\StockAnalyse.Api.csproj")) {
    Write-Error "请在项目根目录运行此脚本"
    Read-Host "按Enter键退出"
    exit 1
}

$skipPython = $false
$skipFrontend = $false

# 检查Python服务
if (-not (Test-Path "python-data-service\stock_data_service.py")) {
    Write-Warning "Python服务目录不存在，跳过Python服务启动"
    $skipPython = $true
}

# 检查前端目录
if (-not (Test-Path "frontend\package.json")) {
    Write-Warning "前端目录不存在，跳过前端启动"
    $skipFrontend = $true
}

# [1/3] 启动Python数据服务
Write-Step "[1/3] 启动Python数据服务..."
if (-not $skipPython) {
    # 尝试多种方式检测Python
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
                        Write-Host "  检测到Python: $version" -ForegroundColor Gray
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
                            Write-Host "  从路径检测到Python: $version" -ForegroundColor Gray
                            break
                        }
                    }
                } catch {
                    continue
                }
            }
        }
    }
    
    if ($pythonExe) {
        # 检查服务是否已经在运行
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:5001/health" -TimeoutSec 1 -UseBasicParsing -ErrorAction Stop
            $serviceRunning = $true
        } catch {
            $serviceRunning = $false
        }
        
        if (-not $serviceRunning) {
            $pythonServicePath = Join-Path $PSScriptRoot "python-data-service"
            
            # 使用检测到的Python可执行文件启动服务
            if ($pythonExe -match "^[a-zA-Z]+$") {
                # 如果是命令名（如python、py），直接使用
                Start-Process $pythonExe -ArgumentList "stock_data_service.py" -WorkingDirectory $pythonServicePath -WindowStyle Minimized
            } else {
                # 如果是完整路径，使用Start-Process
                Start-Process -FilePath $pythonExe -ArgumentList "stock_data_service.py" -WorkingDirectory $pythonServicePath -WindowStyle Minimized
            }
            
            Start-Sleep -Seconds 3
            
            # 检查服务是否启动成功
            try {
                $response = Invoke-WebRequest -Uri "http://localhost:5001/health" -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
                Write-Success "Python服务已启动 (http://localhost:5001)"
            } catch {
                Write-Warning "Python服务可能未完全启动，请检查"
                Write-Host "  提示: 如果服务启动失败，请手动运行: cd python-data-service && python stock_data_service.py" -ForegroundColor Gray
            }
        } else {
            Write-Success "Python服务已在运行"
        }
    } else {
        Write-Warning "未检测到Python，Python服务将无法启动"
        Write-Host "  请先安装Python 3.8+: https://www.python.org/downloads/" -ForegroundColor Yellow
        Write-Host "  或刷新环境变量后重新打开命令行窗口" -ForegroundColor Yellow
        Write-Host "  提示: 安装Python时请勾选 'Add Python to PATH' 选项" -ForegroundColor Gray
        $skipPython = $true
    }
} else {
    Write-Warning "跳过Python服务"
}
Write-Host ""

# [2/3] 启动后端API服务
Write-Step "[2/3] 启动后端API服务..."
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) {
    # 检查后端是否已经在运行
    $apiPort = 5000
    $apiRunning = $false
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:$apiPort/swagger" -TimeoutSec 1 -UseBasicParsing -ErrorAction Stop
        $apiRunning = $true
    } catch {
        # 服务未运行，继续启动
    }

    if (-not $apiRunning) {
        $apiPath = Join-Path $PSScriptRoot "src\StockAnalyse.Api"
        Start-Process dotnet -ArgumentList "run" -WorkingDirectory $apiPath -WindowStyle Minimized
        Start-Sleep -Seconds 5
        
        # 检查服务是否启动成功
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:$apiPort/swagger" -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
            Write-Success "后端API已启动 (http://localhost:$apiPort)"
        } catch {
            Write-Warning "后端API可能仍在启动中，请稍候..."
        }
    } else {
        Write-Success "后端API已在运行"
    }
} else {
    Write-Error "未检测到.NET SDK，请先安装"
    Write-Host "  下载地址: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    Read-Host "按Enter键退出"
    exit 1
}
Write-Host ""

# [3/3] 启动前端开发服务器
Write-Step "[3/3] 启动前端开发服务器..."
if (-not $skipFrontend) {
    $nodeCmd = Get-Command node -ErrorAction SilentlyContinue
    if ($nodeCmd) {
        # 检查前端是否已经在运行
        $frontendPort = 5173
        $frontendRunning = $false
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:$frontendPort" -TimeoutSec 1 -UseBasicParsing -ErrorAction Stop
            $frontendRunning = $true
        } catch {
            # 服务未运行，继续启动
        }
        
        if (-not $frontendRunning) {
            $frontendPath = Join-Path $PSScriptRoot "frontend"
            
            # 检查node_modules是否存在
            if (-not (Test-Path "$frontendPath\node_modules")) {
                Write-Host "  检测到缺少依赖，正在安装..." -ForegroundColor Yellow
                Set-Location $frontendPath
                npm install
                Set-Location $PSScriptRoot
            }
            
            Start-Process npm -ArgumentList "run", "dev" -WorkingDirectory $frontendPath
            Start-Sleep -Seconds 3
            Write-Success "前端服务已启动 (http://localhost:$frontendPort)"
        } else {
            Write-Success "前端服务已在运行"
        }
    } else {
        Write-Warning "未检测到Node.js，跳过前端服务"
        Write-Host "  请先安装Node.js: https://nodejs.org/" -ForegroundColor Yellow
        $skipFrontend = $true
    }
} else {
    Write-Warning "跳过前端服务"
}
Write-Host ""

# 显示启动结果
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  所有服务启动完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "服务地址：" -ForegroundColor Yellow
if (-not $skipPython) {
    Write-Host "  Python数据服务: http://localhost:5001" -ForegroundColor White
}
Write-Host "  后端API服务:    http://localhost:5000" -ForegroundColor White
if (-not $skipFrontend) {
    Write-Host "  前端开发服务器:  http://localhost:5173" -ForegroundColor White
}
Write-Host ""
Write-Host "API文档: http://localhost:5000/swagger" -ForegroundColor Cyan
Write-Host ""
Write-Host "提示：" -ForegroundColor Yellow
Write-Host "  - 每个服务都在独立的窗口中运行" -ForegroundColor Gray
Write-Host "  - 关闭对应窗口即可停止服务" -ForegroundColor Gray
Write-Host "  - 按Enter键关闭此窗口（不会停止服务）" -ForegroundColor Gray
Write-Host ""
Read-Host "按Enter键退出"

