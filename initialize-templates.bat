@echo off
REM 初始化优化选股模板脚本（批处理版本）
REM 使用方法：双击运行或在 CMD 中执行 initialize-templates.bat

echo 正在初始化优化选股模板...
echo.

REM 检查 PowerShell 是否可用
where powershell >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo 错误: 未找到 PowerShell，请使用 PowerShell 执行 initialize-templates.ps1
    pause
    exit /b 1
)

REM 使用 PowerShell 执行请求
powershell -ExecutionPolicy Bypass -Command ^
    "$apiUrl = 'http://localhost:5000/api/ScreenTemplate/initialize-optimized'; ^
    try { ^
        $response = Invoke-RestMethod -Uri $apiUrl -Method POST -ContentType 'application/json'; ^
        Write-Host ''; ^
        Write-Host '模板初始化成功！' -ForegroundColor Green; ^
        Write-Host ('已更新: ' + $response.updated + ' 个模板') -ForegroundColor Cyan; ^
        Write-Host ('已创建: ' + $response.created + ' 个模板') -ForegroundColor Cyan; ^
    } catch { ^
        Write-Host ''; ^
        Write-Host '请求失败！' -ForegroundColor Red; ^
        Write-Host ('错误: ' + $_.Exception.Message) -ForegroundColor Red; ^
        Write-Host ''; ^
        Write-Host '提示: 请确保 API 服务正在运行 (http://localhost:5000)' -ForegroundColor Yellow; ^
        exit 1; ^
    }"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo 完成！
) else (
    echo.
    echo 执行失败，请检查错误信息。
)

pause

