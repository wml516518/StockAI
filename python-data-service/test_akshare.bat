@echo off
chcp 65001 >nul
echo ============================================================
echo 测试AKShare API接口
echo ============================================================
echo.

REM 检查Python是否安装
python --version >nul 2>&1
if errorlevel 1 (
    echo ❌ 未检测到Python，请先安装Python
    pause
    exit /b 1
)

REM 检查是否安装了requests库
python -c "import requests" >nul 2>&1
if errorlevel 1 (
    echo ⚠️  未检测到requests库，正在安装...
    pip install requests
    if errorlevel 1 (
        echo ❌ 安装requests失败
        pause
        exit /b 1
    )
)

echo ✅ 环境检查通过
echo.
echo 请确保Python数据服务已启动（运行 start-service.bat 或 python stock_data_service.py）
echo 然后按任意键开始测试...
pause >nul

echo.
python test_akshare_api.py

echo.
echo 测试完成！
pause

