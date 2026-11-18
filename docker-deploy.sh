#!/bin/bash

# ====================================================
# Docker 快速部署脚本
# ====================================================

set -euo pipefail
cd "$(dirname "$0")"

echo "======================================"
echo "🚀 股票分析系统 - Docker快速部署"
echo "======================================"
echo ""

# 检查Docker是否安装
if ! command -v docker &> /dev/null; then
    echo "❌ Docker未安装，请先安装Docker"
    echo "   安装指南: https://docs.docker.com/engine/install/"
    exit 1
fi

# 检查Docker Compose是否安装
if ! docker compose version &> /dev/null; then
    echo "❌ 请安装支持 --wait 的 Docker Compose v2 或更高版本"
    exit 1
fi

echo "✓ Docker已安装: $(docker --version)"
echo "✓ Docker Compose已安装"
echo ""

# 检查.env文件
if [ ! -f .env ]; then
    echo "⚠️  未找到.env文件，正在创建..."
    cp .env.example .env
    echo "✓ 已创建.env文件"
    echo ""
    echo "默认仅监听 127.0.0.1:18081，可通过 .env 调整本机端口。"
fi

echo "✓ 找到.env配置文件"
echo ""

# 首次运行时创建不含真实凭据的 Python 配置模板
if [ ! -f python-data-service/config.ini ]; then
    cp python-data-service/config.example.ini python-data-service/config.ini
    echo "✓ 已创建 python-data-service/config.ini（TuShare Token 默认为空）"
    echo ""
fi

if ! grep -Eq '^APP_AUTH_USERNAME=.+' .env \
    || ! grep -Eq '^APP_AUTH_PASSWORD=.+' .env; then
    echo "❌ 请先在 .env 中设置 APP_AUTH_USERNAME 和 APP_AUTH_PASSWORD"
    echo "   密码建议使用至少 20 位随机字符，并通过 HTTPS 访问公网入口。"
    echo "   生成密码: printf 'APP_AUTH_PASSWORD=%s\\n' \"\$(openssl rand -hex 24)\""
    exit 1
fi

# 构建并启动服务
echo "🔨 构建Docker镜像（首次运行可能需要几分钟）..."
docker compose config --quiet
docker compose build

echo ""
echo "🚀 启动服务..."
if ! docker compose up -d --wait --wait-timeout 180; then
    docker compose logs --tail=100
    exit 1
fi

echo ""
echo ""
echo "📊 服务状态:"
docker compose ps

echo ""
echo "======================================"
echo "✅ 部署完成！"
echo "======================================"
echo ""
echo "访问地址:"
docker compose port frontend 80
echo ""
echo "常用命令:"
echo "  查看日志: docker compose logs -f"
echo "  停止服务: docker compose stop"
echo "  重启服务: docker compose restart"
echo "  完全停止: docker compose down"
echo ""
echo "详细文档请查看: DOCKER_DEPLOYMENT.md"
echo ""
