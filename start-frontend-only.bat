@echo off
chcp 65001 >nul
echo ========================================
echo   启动前端开发服务器
echo ========================================
echo.

REM 刷新环境变量
set PATH=%PATH%;%ProgramFiles%\nodejs;%ProgramFiles(x86)%\nodejs

REM 检查Node.js
where node >nul 2>&1
if errorlevel 1 (
    echo [错误] 未检测到Node.js
    echo 请先安装Node.js: https://nodejs.org/
    echo 或刷新环境变量后重新打开命令行窗口
    pause
    exit /b 1
)

echo [1/2] 检查Node.js环境...
node --version
npm --version
echo.

REM 检查前端目录
if not exist "frontend\package.json" (
    echo [错误] 前端目录不存在
    pause
    exit /b 1
)

echo [2/2] 启动前端开发服务器...
cd frontend

REM 检查依赖
if not exist "node_modules" (
    echo   检测到缺少依赖，正在安装...
    call npm install
    if errorlevel 1 (
        echo   [错误] 依赖安装失败
        cd ..
        pause
        exit /b 1
    )
)

echo   启动开发服务器...
echo   服务将在 http://localhost:5173 启动
echo   按 Ctrl+C 停止服务
echo.
call npm run dev

cd ..
pause

