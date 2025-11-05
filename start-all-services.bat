@echo off
chcp 65001 >nul 2>&1
echo ========================================
echo   股票分析系统 - 一键启动所有服务
echo ========================================
echo.

REM 启动Python数据服务
if exist "python-data-service\stock_data_service.py" (
    echo [1/3] 启动Python数据服务...
    start "Python数据服务" cmd /k "cd /d %~dp0python-data-service && python stock_data_service.py"
    timeout /t 2 /nobreak >nul
    echo   ✅ Python服务已启动 (http://localhost:5001)
    echo.
) else (
    echo [1/3] ⚠️  跳过Python服务（未找到文件）
    echo.
)

REM 启动后端API服务
if exist "src\StockAnalyse.Api\StockAnalyse.Api.csproj" (
    echo [2/3] 启动后端API服务...
    start "后端API服务" cmd /k "cd /d %~dp0src\StockAnalyse.Api && dotnet run"
    timeout /t 2 /nobreak >nul
    echo   ✅ 后端API服务已启动 (http://localhost:5000)
    echo.
) else (
    echo [2/3] ❌ 后端API服务目录不存在
    echo.
)

REM 启动前端开发服务器
if exist "frontend\package.json" (
    echo [3/3] 启动前端开发服务器...
    start "前端开发服务器" cmd /k "cd /d %~dp0frontend && npm run dev"
    timeout /t 2 /nobreak >nul
    echo   ✅ 前端服务已启动 (http://localhost:5173)
    echo.
) else (
    echo [3/3] ⚠️  跳过前端服务（未找到文件）
    echo.
)

echo ========================================
echo   所有服务启动完成！
echo ========================================
echo.
echo 服务地址：
echo   Python数据服务: http://localhost:5001
echo   后端API服务:    http://localhost:5000
echo   前端开发服务器:  http://localhost:5173
echo.
echo API文档: http://localhost:5000/swagger
echo.
echo 提示：
echo   - 每个服务都在独立的窗口中运行
echo   - 关闭对应窗口即可停止服务
echo   - 按任意键关闭此窗口（不会停止服务）
echo.
pause
