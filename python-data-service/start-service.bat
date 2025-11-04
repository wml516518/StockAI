@echo off
setlocal enabledelayedexpansion
echo ========================================
echo 启动股票数据服务 (Python + AKShare)
echo ========================================
echo.

REM 检查Python是否安装（优先使用python，如果不存在则使用py）
set "PYTHON_CMD="
python --version >nul 2>&1
if not errorlevel 1 (
    set "PYTHON_CMD=python"
) else (
    py --version >nul 2>&1
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
    echo [错误] 未检测到Python，请先安装Python 3.8+
    echo 下载地址: https://www.python.org/downloads/
    pause
    exit /b 1
)

echo [1/3] 检查依赖...
!PYTHON_CMD! -m pip show flask >nul 2>&1
if errorlevel 1 (
    echo [2/3] 安装依赖包...
    !PYTHON_CMD! -m pip install -r requirements.txt
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
!PYTHON_CMD! stock_data_service.py

pause

