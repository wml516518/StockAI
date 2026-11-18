@echo off
REM ====================================================
REM Docker 快速部署脚本 (Windows)
REM ====================================================

echo ======================================
echo 🚀 股票分析系统 - Docker快速部署
echo ======================================
echo.

REM 检查Docker是否安装
docker --version >nul 2>&1
if errorlevel 1 (
    echo ❌ Docker未安装，请先安装Docker Desktop
    echo    下载地址: https://www.docker.com/products/docker-desktop
    pause
    exit /b 1
)

echo ✓ Docker已安装
docker --version
echo.

REM 检查Docker Compose v2
docker compose version >nul 2>&1
if errorlevel 1 (
    echo ❌ 请安装支持 --wait 的 Docker Compose v2
    pause
    exit /b 1
)

REM 检查.env文件
if not exist .env (
    echo ⚠️  未找到.env文件，正在创建...
    copy .env.example .env
    echo ✓ 已创建.env文件
    echo.
    echo ⚠️  请编辑.env文件，设置 APP_AUTH_USERNAME 和 APP_AUTH_PASSWORD
    echo    然后重新运行此脚本
    echo.
    pause
    exit /b 0
)

echo ✓ 找到.env配置文件
echo.

set "APP_AUTH_USERNAME="
set "APP_AUTH_PASSWORD="
for /f "usebackq eol=# tokens=1,* delims==" %%A in (".env") do (
    if /i "%%A"=="APP_AUTH_USERNAME" if not defined APP_AUTH_USERNAME set "APP_AUTH_USERNAME=%%B"
    if /i "%%A"=="APP_AUTH_PASSWORD" if not defined APP_AUTH_PASSWORD set "APP_AUTH_PASSWORD=%%B"
)
if not defined APP_AUTH_USERNAME (
    echo ❌ 请先在 .env 中设置 APP_AUTH_USERNAME
    pause
    exit /b 1
)
if not defined APP_AUTH_PASSWORD (
    echo ❌ 请先在 .env 中设置 APP_AUTH_PASSWORD
    pause
    exit /b 1
)

if not exist python-data-service\config.ini (
    copy python-data-service\config.example.ini python-data-service\config.ini >nul
    echo ✓ 已创建 python-data-service\config.ini（TuShare Token 默认为空）
)
echo.

REM 构建并启动服务
echo 🔨 构建Docker镜像（首次运行可能需要几分钟）...
docker compose config --quiet
if errorlevel 1 exit /b 1
docker compose build
if errorlevel 1 exit /b 1

echo.
echo 🚀 启动服务...
docker compose up -d --wait --wait-timeout 180
if errorlevel 1 (
    docker compose logs --tail=100
    pause
    exit /b 1
)

REM 检查服务状态
echo.
echo 📊 服务状态:
docker compose ps

echo.
echo ======================================
echo ✅ 部署完成！
echo ======================================
echo.
echo 访问地址:
docker compose port frontend 80
echo   打开页面时请输入 .env 中的 APP_AUTH_USERNAME / APP_AUTH_PASSWORD
echo.
echo 常用命令:
echo   查看日志: docker compose logs -f
echo   停止服务: docker compose stop
echo   重启服务: docker compose restart
echo   完全停止: docker compose down
echo.
echo 详细文档请查看: DOCKER_DEPLOYMENT.md
echo.
pause
