@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
echo ========================================
echo   股票分析系统 - 一键启动所有服务
echo ========================================
echo.

REM 检查是否在正确的目录
if not exist "src\StockAnalyse.Api\StockAnalyse.Api.csproj" (
    echo [错误] 请在项目根目录运行此脚本
    pause
    exit /b 1
)

REM 检查Python服务目录
if not exist "python-data-service\stock_data_service.py" (
    echo [警告] Python服务目录不存在，跳过Python服务启动
    set SKIP_PYTHON=1
) else (
    set SKIP_PYTHON=0
)

REM 检查前端目录
if not exist "frontend\package.json" (
    echo [警告] 前端目录不存在，跳过前端启动
    set SKIP_FRONTEND=1
) else (
    set SKIP_FRONTEND=0
)

echo [1/3] 启动Python数据服务...
if "%SKIP_PYTHON%"=="0" (
    echo   检查Python服务...
    start "Python数据服务" /MIN cmd /c "cd python-data-service && python stock_data_service.py"
    timeout /t 3 /nobreak >nul
    echo   ✅ Python服务已启动 (http://localhost:5001)
) else (
    echo   ⚠️  跳过Python服务
)
echo.

echo [2/3] 启动后端API服务...
echo   检查.NET环境...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo   ❌ 未检测到.NET SDK，请先安装
    echo   下载地址: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)
echo   启动后端API...
start "后端API服务" /MIN cmd /c "cd src\StockAnalyse.Api && dotnet run"
timeout /t 5 /nobreak >nul
echo   ✅ 后端API已启动 (http://localhost:5000)
echo.

echo [3/3] 启动前端开发服务器...
if "%SKIP_FRONTEND%"=="0" (
    echo   检查Node.js环境...
    REM 刷新环境变量以识别Node.js
    REM 先尝试从PATH中查找（最简单可靠）
    where node >nul 2>&1
    if errorlevel 1 (
        REM 如果PATH中没有，尝试常见安装路径
        set "NODE_FOUND=0"
        if exist "%ProgramFiles%\nodejs\node.exe" (
            set "PATH=%PATH%;%ProgramFiles%\nodejs"
            set "NODE_FOUND=1"
        )
        REM 使用延迟扩展处理ProgramFiles(x86)
        set "PF86=%ProgramFiles(x86)%"
        if exist "!PF86!\nodejs\node.exe" (
            set "PATH=%PATH%;!PF86!\nodejs"
            set "NODE_FOUND=1"
        )
        if "!NODE_FOUND!"=="0" (
            echo   ⚠️  未检测到Node.js，前端服务将无法启动
            echo   请先安装Node.js: https://nodejs.org/
            echo   或刷新环境变量后重新打开命令行窗口
            set SKIP_FRONTEND=1
        )
    )
    
    if "%SKIP_FRONTEND%"=="0" (
        echo   检查前端依赖...
        if not exist "frontend\node_modules" (
            echo   检测到缺少依赖，正在安装...
            cd frontend
            call npm install
            if errorlevel 1 (
                echo   ❌ 依赖安装失败，请检查网络连接
                cd ..
                set SKIP_FRONTEND=1
            ) else (
                cd ..
            )
        )
        
        if "%SKIP_FRONTEND%"=="0" (
            echo   启动前端开发服务器...
            REM 使用前端目录中的启动脚本（更可靠）
            if exist "frontend\start-dev.bat" (
                start "前端开发服务器" cmd /k "cd /d %~dp0frontend && start-dev.bat"
            ) else (
                REM 备用方案：直接启动
                REM 刷新环境变量（确保npm可用）
                if exist "%ProgramFiles%\nodejs" (
                    set "PATH=%PATH%;%ProgramFiles%\nodejs"
                )
                REM 使用延迟扩展处理ProgramFiles(x86)
                set "PF86=%ProgramFiles(x86)%"
                if exist "!PF86!\nodejs" (
                    set "PATH=%PATH%;!PF86!\nodejs"
                )
                
                REM 查找npm.cmd的完整路径
                where npm.cmd >nul 2>&1
                if errorlevel 1 (
                    REM 如果找不到npm，尝试直接使用node运行npm
                    echo   ⚠️  警告: 无法找到npm命令，尝试使用node直接运行...
                    if exist "%ProgramFiles%\nodejs\npm.cmd" (
                        start "前端开发服务器" cmd /k "cd /d %~dp0frontend && echo 正在启动前端开发服务器... && \"%ProgramFiles%\nodejs\npm.cmd\" run dev"
                    ) else if exist "!PF86!\nodejs\npm.cmd" (
                        start "前端开发服务器" cmd /k "cd /d %~dp0frontend && echo 正在启动前端开发服务器... && \"!PF86!\nodejs\npm.cmd\" run dev"
                    ) else (
                        echo   ❌ 无法找到npm，请手动启动前端服务
                        set SKIP_FRONTEND=1
                    )
                ) else (
                    REM 使用/k保持窗口打开，方便查看日志和错误
                    start "前端开发服务器" cmd /k "cd /d %~dp0frontend && echo 正在启动前端开发服务器... && echo 如果看到错误，请检查Node.js环境变量 && npm run dev"
                )
            )
            timeout /t 5 /nobreak >nul
            echo   ✅ 前端服务启动命令已执行 (http://localhost:5173)
            echo   💡 提示: 前端服务需要几秒钟启动，请查看"前端开发服务器"窗口确认状态
            echo   💡 如果前端未启动，请检查"前端开发服务器"窗口中的错误信息
        )
    )
) else (
    echo   ⚠️  跳过前端服务
)
echo.

echo ========================================
echo   所有服务启动完成！
echo ========================================
echo.
echo 服务地址：
if "%SKIP_PYTHON%"=="0" (
    echo   Python数据服务: http://localhost:5001
)
echo   后端API服务:    http://localhost:5000
if "%SKIP_FRONTEND%"=="0" (
    echo   前端开发服务器:  http://localhost:5173
)
echo.
echo API文档: http://localhost:5000/swagger
echo.
echo 提示：
echo   - 每个服务都在独立的窗口中运行
echo   - 关闭对应窗口即可停止服务
echo   - 按任意键关闭此窗口（不会停止服务）
echo.
pause

