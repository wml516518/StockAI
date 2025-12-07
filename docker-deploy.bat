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

REM 检查.env文件
if not exist .env (
    echo ⚠️  未找到.env文件，正在创建...
    copy .env.example .env
    echo ✓ 已创建.env文件
    echo.
    echo ⚠️  请编辑.env文件，填写必要的配置（特别是GEMINI_API_KEY）
    echo    然后重新运行此脚本
    echo.
    pause
    exit /b 0
)

echo ✓ 找到.env配置文件
echo.

REM 创建必要的目录
echo 📁 创建数据目录...
if not exist data mkdir data
if not exist logs mkdir logs
if not exist logs\backend mkdir logs\backend
if not exist logs\frontend mkdir logs\frontend
if not exist logs\python mkdir logs\python
echo ✓ 目录创建完成
echo.

REM 构建并启动服务
echo 🔨 构建Docker镜像（首次运行可能需要几分钟）...
docker-compose build

echo.
echo 🚀 启动服务...
docker-compose up -d

echo.
echo ⏳ 等待服务启动...
timeout /t 10 /nobreak >nul

REM 检查服务状态
echo.
echo 📊 服务状态:
docker-compose ps

echo.
echo ======================================
echo ✅ 部署完成！
echo ======================================
echo.
echo 访问地址:
echo   🌐 前端: http://localhost
echo   🔧 后端API: http://localhost/api
echo.
echo 常用命令:
echo   查看日志: docker-compose logs -f
echo   停止服务: docker-compose stop
echo   重启服务: docker-compose restart
echo   完全停止: docker-compose down
echo.
echo 详细文档请查看: DOCKER_DEPLOYMENT.md
echo.
pause
