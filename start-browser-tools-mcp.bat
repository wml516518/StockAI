@echo off
chcp 65001 >nul
echo ========================================
echo 启动 BrowserTools MCP 服务器
echo ========================================
echo.
echo 服务器将在端口 3025 上启动
echo 按 Ctrl+C 停止服务器
echo.
echo ========================================
echo.

REM 检查 Node.js 是否安装
where node >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [错误] 未检测到 Node.js
    echo 请先安装 Node.js: https://nodejs.org/
    echo 或者运行: winget install OpenJS.NodeJS.LTS
    pause
    exit /b 1
)

echo [信息] 正在启动 BrowserTools MCP 服务器...
echo.

REM 设置环境变量
set BROWSER_TOOLS_HOST=127.0.0.1
set BROWSER_TOOLS_PORT=3025

REM 启动 MCP 服务器
npx @agentdeskai/browser-tools-mcp@latest

pause

