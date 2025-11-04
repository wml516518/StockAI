@echo off
echo ========================================
echo 启动股票数据服务 (Python + AKShare)
echo ========================================
echo.

REM 检查Python是否安装
python --version >nul 2>&1
if errorlevel 1 (
    echo [错误] 未检测到Python，请先安装Python 3.8+
    echo 下载地址: https://www.python.org/downloads/
    pause
    exit /b 1
)

echo [1/3] 检查依赖...
pip show flask >nul 2>&1
if errorlevel 1 (
    echo [2/3] 安装依赖包...
    pip install -r requirements.txt
    if errorlevel 1 (
        echo [错误] 依赖安装失败，请检查网络连接
        pause
        exit /b 1
    )
) else (
    echo [2/3] 依赖已安装
)

echo [3/3] 启动服务...
echo.
echo 服务将在 http://localhost:5001 启动
echo 按 Ctrl+C 停止服务
echo.
python stock_data_service.py

pause

