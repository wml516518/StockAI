#!/bin/bash

# ====================================================
# Docker 快速部署脚本
# ====================================================

set -e

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
if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
    echo "❌ Docker Compose未安装，请先安装Docker Compose"
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
    echo "⚠️  请编辑.env文件，填写必要的配置（特别是GEMINI_API_KEY）"
    echo "   然后重新运行此脚本"
    echo ""
    echo "使用命令编辑: nano .env"
    exit 0
fi

echo "✓ 找到.env配置文件"
echo ""

# 创建必要的目录
echo "📁 创建数据目录..."
mkdir -p data logs/backend logs/frontend logs/python
echo "✓ 目录创建完成"
echo ""

# 构建并启动服务
echo "🔨 构建Docker镜像（首次运行可能需要几分钟）..."
docker-compose build

echo ""
echo "🚀 启动服务..."
docker-compose up -d

echo ""
echo "⏳ 等待服务启动..."
sleep 10

# 检查服务状态
echo ""
echo "📊 服务状态:"
docker-compose ps

echo ""
echo "======================================"
echo "✅ 部署完成！"
echo "======================================"
echo ""
echo "访问地址:"
echo "  🌐 前端: http://localhost"
echo "  🔧 后端API: http://localhost/api"
echo ""
echo "常用命令:"
echo "  查看日志: docker-compose logs -f"
echo "  停止服务: docker-compose stop"
echo "  重启服务: docker-compose restart"
echo "  完全停止: docker-compose down"
echo ""
echo "详细文档请查看: DOCKER_DEPLOYMENT.md"
echo ""
