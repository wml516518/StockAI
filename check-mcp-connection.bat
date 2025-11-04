@echo off
chcp 65001 >nul
echo ========================================
echo 检查 BrowserTools MCP 连接状态
echo ========================================
echo.

REM 检查端口是否在监听
echo [1/3] 检查端口 3025 是否在监听...
netstat -an | findstr ":3025" >nul
if %ERRORLEVEL% EQU 0 (
    echo ✅ 端口 3025 正在监听
) else (
    echo ❌ 端口 3025 未在监听
    echo    请先启动 BrowserTools MCP 服务器
    echo    运行: start-browser-tools-mcp.bat
)
echo.

REM 尝试连接测试
echo [2/3] 测试连接到 http://localhost:3025...
curl -s http://localhost:3025 >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo ✅ 可以连接到服务器
) else (
    echo ❌ 无法连接到服务器
    echo    请确保 BrowserTools MCP 服务器正在运行
)
echo.

REM 检查 Chrome 扩展
echo [3/3] 检查 Chrome 扩展...
echo    请手动检查：
echo    1. 打开 Chrome 浏览器
echo    2. 访问 chrome://extensions/
echo    3. 确认 BrowserTools MCP 扩展已启用
echo.

echo ========================================
echo 检查完成
echo ========================================
echo.
pause

