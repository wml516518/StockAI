@echo off
REM 尝试设置UTF-8编码（如果chcp不可用则忽略）
chcp 65001 >nul 2>&1
setlocal enabledelayedexpansion
echo ========================================
echo   启动前端开发服务器
echo ========================================
echo.

REM 刷新环境变量以识别Node.js（从系统注册表读取最新PATH）
for /f "tokens=2*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH 2^>nul') do set "SYSTEM_PATH=%%b"
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "USER_PATH=%%b"
if defined SYSTEM_PATH set "PATH=!SYSTEM_PATH!"
if defined USER_PATH set "PATH=!PATH%;!USER_PATH!"

REM 添加常见安装路径到PATH（使用延迟扩展）
if exist "%ProgramFiles%\nodejs\node.exe" (
    set "PATH=!PATH%;%ProgramFiles%\nodejs"
)
if exist "%ProgramFiles(x86)%\nodejs\node.exe" (
    set "PATH=!PATH%;%ProgramFiles(x86)%\nodejs"
)

REM 检查Node.js（先检查常见路径，再检查PATH）
set "NODE_FOUND=0"
if exist "%ProgramFiles%\nodejs\node.exe" (
    set "NODE_FOUND=1"
) else if exist "%ProgramFiles(x86)%\nodejs\node.exe" (
    set "NODE_FOUND=1"
)

if "!NODE_FOUND!"=="0" (
    where node >nul 2>&1
    if not errorlevel 1 (
        set "NODE_FOUND=1"
    )
)

if "!NODE_FOUND!"=="0" (
    echo [错误] 未检测到Node.js
    echo.
    echo [解决方案]
    echo [步骤1] 确保已安装Node.js: https://nodejs.org/
    echo [步骤2] 刷新环境变量，运行以下命令
    echo    set PATH=%%PATH%%^;%%ProgramFiles%%\nodejs
    echo [步骤3] 或重新打开命令行窗口
    echo.
    pause
    exit /b 1
)

echo [1/2] 检查环境...
node --version
npm --version
echo.

echo [2/2] 启动开发服务器...
echo.

REM 检查依赖
if not exist "node_modules" (
    echo 检测到缺少依赖，正在安装...
    call npm install
    if errorlevel 1 (
        echo [错误] 依赖安装失败
        pause
        exit /b 1
    )
    echo.
)

echo 服务将在 http://localhost:5173 启动
echo 按 Ctrl+C 停止服务
echo.
call npm run dev

pause
