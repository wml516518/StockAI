@echo off
chcp 65001 >nul 2>&1
echo ========================================
echo   检查服务运行状态
echo ========================================
echo.

REM 检查后端API服务
echo [检查后端API服务] http://localhost:5000
curl -s http://localhost:5000/health >nul 2>&1
if %errorlevel% == 0 (
    echo   ✅ 后端API服务正在运行
) else (
    echo   ❌ 后端API服务未运行
    echo   💡 请运行: cd src\StockAnalyse.Api ^&^& dotnet run
)
echo.

REM 检查Python服务
echo [检查Python数据服务] http://localhost:5001
curl -s http://localhost:5001/health >nul 2>&1
if %errorlevel% == 0 (
    echo   ✅ Python数据服务正在运行
) else (
    echo   ⚠️  Python数据服务未运行（可选）
    echo   💡 如需使用AKShare数据，请运行: cd python-data-service ^&^& python stock_data_service.py
)
echo.

REM 检查前端服务
echo [检查前端开发服务器] http://localhost:5173
curl -s http://localhost:5173 >nul 2>&1
if %errorlevel% == 0 (
    echo   ✅ 前端服务正在运行
) else (
    echo   ⚠️  前端服务未运行
    echo   💡 请运行: cd frontend ^&^& npm run dev
)
echo.

echo ========================================
echo   快速启动所有服务：
echo   运行 start-all-services.bat
echo ========================================
echo.
pause

