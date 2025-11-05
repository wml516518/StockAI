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
    echo 请先安装.NET 8.0 SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo [1/2] 检查.NET环境...
dotnet --version
echo.

echo [2/2] 启动后端API服务...
cd src\StockAnalyse.Api

echo   服务将在 http://localhost:5000 启动
echo   API文档: http://localhost:5000/swagger
echo   按 Ctrl+C 停止服务
echo.

dotnet run

cd ..\..
pause

