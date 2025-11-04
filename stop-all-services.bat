@echo off
chcp 65001 >nul
echo ========================================
echo   停止所有服务
echo ========================================
echo.

echo 正在停止服务...
echo.

REM 停止Python服务
echo [1/3] 停止Python数据服务...
taskkill /FI "WINDOWTITLE eq Python数据服务*" /F >nul 2>&1
taskkill /FI "IMAGENAME eq python.exe" /FI "COMMANDLINE eq *stock_data_service.py*" /F >nul 2>&1
if errorlevel 1 (
    echo   ⚠️  未找到Python服务进程
) else (
    echo   ✅ Python服务已停止
)
echo.

REM 停止后端API服务
echo [2/3] 停止后端API服务...
taskkill /FI "WINDOWTITLE eq 后端API服务*" /F >nul 2>&1
taskkill /FI "IMAGENAME eq dotnet.exe" /FI "COMMANDLINE eq *StockAnalyse.Api*" /F >nul 2>&1
if errorlevel 1 (
    echo   ⚠️  未找到后端API服务进程
) else (
    echo   ✅ 后端API服务已停止
)
echo.

REM 停止前端服务
echo [3/3] 停止前端开发服务器...
taskkill /FI "WINDOWTITLE eq 前端开发服务器*" /F >nul 2>&1
taskkill /FI "IMAGENAME eq node.exe" /FI "COMMANDLINE eq *vite*" /F >nul 2>&1
if errorlevel 1 (
    echo   ⚠️  未找到前端服务进程
) else (
    echo   ✅ 前端服务已停止
)
echo.

echo ========================================
echo   所有服务已停止
echo ========================================
echo.
pause

