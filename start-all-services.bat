@echo off
REM 尝试设置UTF-8编码（如果chcp不可用则忽略）
chcp 65001 >nul 2>&1
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
    echo   检查Python环境...
    REM 刷新环境变量以识别Python
    for /f "tokens=2*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH 2^>nul') do set "SYSTEM_PATH=%%b"
    for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "USER_PATH=%%b"
    if defined SYSTEM_PATH set "PATH=!SYSTEM_PATH!"
    if defined USER_PATH set "PATH=!PATH%;!USER_PATH!"
    
    REM 检测Python命令（优先使用python，如果不存在则使用py）
    set "PYTHON_CMD="
    where python >nul 2>&1
    if not errorlevel 1 (
        set "PYTHON_CMD=python"
    ) else (
        where py >nul 2>&1
        if not errorlevel 1 (
            set "PYTHON_CMD=py"
        ) else (
            REM 尝试常见安装路径
            if exist "%LOCALAPPDATA%\Programs\Python\Python312\python.exe" (
                set "PYTHON_CMD=%LOCALAPPDATA%\Programs\Python\Python312\python.exe"
            ) else if exist "C:\Python312\python.exe" (
                set "PYTHON_CMD=C:\Python312\python.exe"
            )
        )
    )
    
    if "!PYTHON_CMD!"=="" (
        echo   [警告] 未检测到Python，Python服务将无法启动
        echo   请先安装Python https://www.python.org/downloads/
        echo   或刷新环境变量后重新打开命令行窗口
        set SKIP_PYTHON=1
    ) else (
        echo   使用Python命令: !PYTHON_CMD!
        echo   检查Python依赖...
        !PYTHON_CMD! -m pip show flask >nul 2>&1
        if errorlevel 1 (
            echo   检测到缺少依赖，正在安装...
            cd python-data-service
            !PYTHON_CMD! -m pip install -r requirements.txt --quiet
            if errorlevel 1 (
                echo   ❌ 依赖安装失败，请检查网络连接
                cd ..
                set SKIP_PYTHON=1
            ) else (
                echo   ✅ 依赖安装完成
                cd ..
            )
        )
        
        if "!SKIP_PYTHON!"=="0" (
            echo   启动Python服务...
            REM 检查是否有Python服务占用端口5001
            netstat -ano | findstr ":5001" >nul 2>&1
            if not errorlevel 1 (
                echo   检测到端口5001已被占用，正在查找并停止相关进程...
                for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":5001" ^| findstr "LISTENING"') do (
                    taskkill /F /PID %%a >nul 2>&1
                )
                ping 127.0.0.1 -n 2 >nul
                echo   相关进程已停止
            )
            start "Python数据服务" cmd /k "cd /d %~dp0python-data-service && echo 正在启动Python数据服务... && echo 服务地址: http://localhost:5001 && echo 按 Ctrl+C 停止服务 && echo. && !PYTHON_CMD! stock_data_service.py"
            ping 127.0.0.1 -n 4 >nul
            echo   ✅ Python服务启动命令已执行 (http://localhost:5001)
            echo   💡 提示: Python服务需要几秒钟启动，请查看"Python数据服务"窗口确认状态
        )
    )
) else (
    echo   ⚠️  跳过Python服务
)
echo.

echo [2/3] 启动后端API服务...
echo   检查.NET环境...
REM 刷新环境变量以识别.NET SDK
for /f "tokens=2*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH 2^>nul') do set "SYSTEM_PATH=%%b"
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "USER_PATH=%%b"
if defined SYSTEM_PATH set "PATH=!SYSTEM_PATH!"
if defined USER_PATH set "PATH=!PATH%;!USER_PATH!"

REM 检测dotnet命令（先检查常见路径，再检查PATH）
set "DOTNET_FOUND=0"
if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "DOTNET_FOUND=1"
    set "DOTNET_PATH=%ProgramFiles%\dotnet"
) else if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
    set "DOTNET_FOUND=1"
    set "DOTNET_PATH=%ProgramFiles(x86)%\dotnet"
)

if "!DOTNET_FOUND!"=="0" (
    where dotnet >nul 2>&1
    if not errorlevel 1 (
        set "DOTNET_FOUND=1"
    )
)

if "!DOTNET_FOUND!"=="0" (
    echo.
    echo   [错误] 未检测到.NET SDK，请先安装
    echo   [提示] 下载地址 https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

REM 验证dotnet版本
if defined DOTNET_PATH (
    "!DOTNET_PATH!\dotnet.exe" --version >nul 2>&1
) else (
    dotnet --version >nul 2>&1
)
if errorlevel 1 (
    echo.
    echo   [错误] .NET SDK检测失败，请检查安装
    echo   [提示] 下载地址 https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)
echo   启动后端API...
REM 检查是否有后端服务已经在运行
tasklist /FI "IMAGENAME eq StockAnalyse.Api.exe" 2>nul | find /I /N "StockAnalyse.Api.exe">nul
if "%ERRORLEVEL%"=="0" (
    echo   检测到后端服务进程正在运行，正在停止旧进程...
    taskkill /F /IM StockAnalyse.Api.exe >nul 2>&1
    ping 127.0.0.1 -n 3 >nul
    echo   旧进程已停止
)
REM 检查端口5000是否被占用
netstat -ano | findstr ":5000" >nul 2>&1
if not errorlevel 1 (
    echo   检测到端口5000已被占用，正在查找并停止相关进程...
    for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":5000" ^| findstr "LISTENING"') do (
        taskkill /F /PID %%a >nul 2>&1
    )
    ping 127.0.0.1 -n 2 >nul
    echo   相关进程已停止
)

REM 使用临时批处理文件来避免引号嵌套问题
set "TEMP_BATCH=%TEMP%\start_backend_%RANDOM%.bat"
if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    REM 创建临时批处理文件
    (
        echo @echo off
        echo cd /d %~dp0src\StockAnalyse.Api
        echo echo 正在启动后端API服务...
        echo echo 服务地址: http://localhost:5000
        echo echo API文档: http://localhost:5000/swagger
        echo echo 按 Ctrl+C 停止服务
        echo echo.
        echo "%ProgramFiles%\dotnet\dotnet.exe" run
    ) > "%TEMP_BATCH%"
    start "后端API服务" cmd /k ""%TEMP_BATCH%""
) else if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
    REM 创建临时批处理文件
    (
        echo @echo off
        echo cd /d %~dp0src\StockAnalyse.Api
        echo echo 正在启动后端API服务...
        echo echo 服务地址: http://localhost:5000
        echo echo API文档: http://localhost:5000/swagger
        echo echo 按 Ctrl+C 停止服务
        echo echo.
        echo "%ProgramFiles(x86)%\dotnet\dotnet.exe" run
    ) > "%TEMP_BATCH%"
    start "后端API服务" cmd /k ""%TEMP_BATCH%""
) else (
    REM 尝试直接使用dotnet（假设已在PATH中）
    start "后端API服务" cmd /k "cd /d %~dp0src\StockAnalyse.Api && echo 正在启动后端API服务... && echo 服务地址: http://localhost:5000 && echo API文档: http://localhost:5000/swagger && echo 按 Ctrl+C 停止服务 && echo. && dotnet run"
)
ping 127.0.0.1 -n 6 >nul
echo   [成功] 后端API启动命令已执行 (http://localhost:5000)
echo   [提示] 后端服务需要几秒钟启动，请查看"后端API服务"窗口确认状态

echo [3/3] 启动前端开发服务器...
if "%SKIP_FRONTEND%"=="0" (
    echo   检查Node.js环境...
    REM 刷新环境变量以识别Node.js（从系统注册表读取最新PATH）
    for /f "tokens=2*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH 2^>nul') do set "SYSTEM_PATH=%%b"
    for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "USER_PATH=%%b"
    if defined SYSTEM_PATH set "PATH=!SYSTEM_PATH!"
    if defined USER_PATH set "PATH=!PATH%;!USER_PATH!"
    
    REM 先尝试从PATH中查找，再检查常见安装路径
    set "NODE_FOUND=0"
    
    REM 先检查常见安装路径
    if exist "%ProgramFiles%\nodejs\node.exe" (
        set "PATH=!PATH%;%ProgramFiles%\nodejs"
        set "NODE_FOUND=1"
    )
    REM 使用延迟扩展处理ProgramFiles(x86)
    set "PF86=%ProgramFiles(x86)%"
    if exist "!PF86!\nodejs\node.exe" (
        set "PATH=!PATH%;!PF86!\nodejs"
        set "NODE_FOUND=1"
    )
    
    REM 如果还没找到，检查PATH中的node
    if "!NODE_FOUND!"=="0" (
        where node >nul 2>&1
        if not errorlevel 1 (
            set "NODE_FOUND=1"
        )
    )
    
    if "!NODE_FOUND!"=="0" (
        echo.
        echo   [警告] 未检测到Node.js，前端服务将无法启动
        echo   [提示] 请先安装Node.js: https://nodejs.org/
        echo   [提示] 安装后请刷新环境变量或重新打开命令行窗口
        echo.
        set SKIP_FRONTEND=1
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
            REM 检查是否有Node.js进程占用端口5173（前端开发服务器）
            netstat -ano | findstr ":5173" >nul 2>&1
            if not errorlevel 1 (
                echo   检测到端口5173已被占用，正在查找并停止相关进程...
                for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":5173" ^| findstr "LISTENING"') do (
                    taskkill /F /PID %%a >nul 2>&1
                )
                ping 127.0.0.1 -n 2 >nul
                echo   相关进程已停止
            )
            REM 使用前端目录中的启动脚本（更可靠）
            if exist "frontend\start-dev.bat" (
                REM 直接调用前端启动脚本，脚本内部会处理环境变量
                start "前端开发服务器" cmd /k "cd /d %~dp0frontend && start-dev.bat"
            ) else (
                REM 备用方案：直接启动
                REM 再次刷新环境变量（确保npm可用）
                for /f "tokens=2*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH 2^>nul') do set "SYSTEM_PATH=%%b"
                for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "USER_PATH=%%b"
                if defined SYSTEM_PATH set "PATH=!SYSTEM_PATH!"
                if defined USER_PATH set "PATH=!PATH%;!USER_PATH!"
                
                REM 如果PATH中没有，尝试常见安装路径
                if exist "%ProgramFiles%\nodejs" (
                    set "PATH=!PATH%;%ProgramFiles%\nodejs"
                )
                REM 使用延迟扩展处理ProgramFiles(x86)
                set "PF86=%ProgramFiles(x86)%"
                if exist "!PF86!\nodejs" (
                    set "PATH=!PATH%;!PF86!\nodejs"
                )
                
                REM 查找npm.cmd的完整路径
                where npm.cmd >nul 2>&1
                if errorlevel 1 (
                    REM 如果找不到npm，尝试直接使用node运行npm
                    echo   ⚠️  警告: 无法找到npm命令，尝试使用node直接运行...
                    set "TEMP_FRONTEND_BATCH=%TEMP%\start_frontend_%RANDOM%.bat"
                    if exist "%ProgramFiles%\nodejs\npm.cmd" (
                        REM 创建临时批处理文件
                        (
                            echo @echo off
                            echo cd /d %~dp0frontend
                            echo echo 正在启动前端开发服务器...
                            echo "%ProgramFiles%\nodejs\npm.cmd" run dev
                        ) > "%TEMP_FRONTEND_BATCH%"
                        start "前端开发服务器" cmd /k ""%TEMP_FRONTEND_BATCH%""
                    ) else if exist "%ProgramFiles(x86)%\nodejs\npm.cmd" (
                        REM 创建临时批处理文件
                        (
                            echo @echo off
                            echo cd /d %~dp0frontend
                            echo echo 正在启动前端开发服务器...
                            echo "%ProgramFiles(x86)%\nodejs\npm.cmd" run dev
                        ) > "%TEMP_FRONTEND_BATCH%"
                        start "前端开发服务器" cmd /k ""%TEMP_FRONTEND_BATCH%""
                    ) else (
                        echo   ❌ 无法找到npm，请手动启动前端服务
                        set SKIP_FRONTEND=1
                    )
                ) else (
                    REM 使用/k保持窗口打开，方便查看日志和错误
                    start "前端开发服务器" cmd /k "cd /d %~dp0frontend && echo 正在启动前端开发服务器... && echo 如果看到错误，请检查Node.js环境变量 && npm run dev"
                )
            )
            ping 127.0.0.1 -n 6 >nul
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

