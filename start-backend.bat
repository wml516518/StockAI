@echo off
chcp 65001 >nul 2>&1
echo ========================================
echo   启动后端API服务
echo ========================================
echo.

REM 检查后端目录
if not exist "src\StockAnalyse.Api\StockAnalyse.Api.csproj" (
    echo [错误] 后端API项目目录不存在
    pause
    exit /b 1
)

REM 检查.NET SDK
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [错误] 未检测到.NET SDK
    echo 请先安装.NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

echo [1/2] 检查.NET环境...
dotnet --version
echo.

REM 读取根目录 .env 中的访问凭据
if exist "%~dp0.env" (
    for /f "usebackq eol=# tokens=1,* delims==" %%A in ("%~dp0.env") do (
        if /i "%%A"=="APP_AUTH_USERNAME" if not defined APP_AUTH_USERNAME set "APP_AUTH_USERNAME=%%B"
        if /i "%%A"=="APP_AUTH_PASSWORD" if not defined APP_AUTH_PASSWORD set "APP_AUTH_PASSWORD=%%B"
    )
)
if not defined APP_AUTH_USERNAME (
    set "APP_AUTH_USERNAME=admin"
)
if not defined APP_AUTH_PASSWORD (
    set "APP_AUTH_PASSWORD=StockAI@2026"
)

echo [2/2] 启动后端API服务...
cd src\StockAnalyse.Api

echo   服务将在 http://localhost:5000 启动
echo   API文档: http://localhost:5000/swagger
echo   按 Ctrl+C 停止服务
echo.

dotnet run

cd ..\..
pause

