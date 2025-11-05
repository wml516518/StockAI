@echo off
chcp 65001 >nul 2>&1
cd /d %~dp0
echo ========================================
echo 启动股票数据服务 (Python + AKShare)
echo ========================================
echo.
REM 检测Python命令
python --version >nul 2>&1
if not errorlevel 1 (
    echo 使用命令: python
    echo 服务将在 http://localhost:5001 启动
    echo 按 Ctrl+C 停止服务
    echo.
    python stock_data_service.py
    goto :end
)
py --version >nul 2>&1
if not errorlevel 1 (
    echo 使用命令: py
    echo 服务将在 http://localhost:5001 启动
    echo 按 Ctrl+C 停止服务
    echo.
    py stock_data_service.py
    goto :end
)
echo [错误] 未检测到Python，请先安装Python 3.8+
echo 下载地址: https://www.python.org/downloads/
echo.
echo 提示: 安装Python时请勾选 'Add Python to PATH' 选项
echo.
pause
:end

