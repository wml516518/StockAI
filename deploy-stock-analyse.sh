#!/bin/bash

# ====================================================
# 阿里云Linux服务器股票分析系统完整部署脚本
# StockAnalyse Deployment Script for Alibaba Cloud Linux
# ====================================================

set -e

echo "此旧版裸机脚本已停用：未适配 .NET 10 与访问认证。请参阅 DOCKER_DEPLOYMENT.md，运行 bash docker-deploy.sh。" >&2
exit 1

# 配置变量 - 请根据实际情况修改
GITHUB_REPO="https://github.com/your-username/StockAnalyse.git"
DOMAIN_NAME="your-domain.com"
SERVER_IP=$(curl -s ifconfig.me || echo "your-server-ip")

echo "=== 股票分析系统部署开始 ==="
echo "仓库地址: $GITHUB_REPO"
echo "域名: $DOMAIN_NAME"
echo "服务器IP: $SERVER_IP"
echo ""

# 1. 系统更新和安装基础工具
echo "步骤1/17: 系统更新和基础工具安装..."
sudo yum update -y
sudo yum install -y wget curl git unzip
echo "✓ 基础工具安装完成"
echo ""

# 2. 安装.NET 8 SDK
echo "步骤2/17: 安装.NET 8 SDK..."
sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm
sudo yum install -y dotnet-sdk-8.0
dotnet --version
echo "✓ .NET 8 SDK安装完成"
echo ""

# 3. 安装Node.js 18
echo "步骤3/17: 安装Node.js 18..."
curl -fsSL https://rpm.nodesource.com/setup_18.x | sudo bash -
sudo yum install -y nodejs
node --version
npm --version
echo "✓ Node.js 18安装完成"
echo ""

# 4. 安装Python 3.8+
echo "步骤4/17: 安装Python..."
sudo yum install -y python3 python3-pip python3-devel
python3 --version
pip3 --version
echo "✓ Python安装完成"
echo ""

# 5. 创建项目目录
echo "步骤5/17: 创建项目目录..."
sudo mkdir -p /opt/stock-analyse
sudo chown -R $USER:$USER /opt/stock-analyse
cd /opt/stock-analyse
echo "✓ 项目目录创建完成: /opt/stock-analyse"
echo ""

# 6. 从GitHub克隆代码
echo "步骤6/17: 克隆代码..."
git clone $GITHUB_REPO .
echo "✓ 代码克隆完成"
echo ""

# 7. 前端构建
echo "步骤7/17: 前端构建..."
cd frontend
npm install
npm run build
cd ..
echo "✓ 前端构建完成"
echo ""

# 8. 后端发布
echo "步骤8/17: 后端发布..."
cd src/StockAnalyse.Api
dotnet restore
dotnet publish -c Release -o /opt/stock-analyse/publish/backend
cd ../..
echo "✓ 后端发布完成"
echo ""

# 9. Python服务配置
echo "步骤9/17: Python服务配置..."
cd python-data-service
pip3 install -r requirements.txt
cd ..
echo "✓ Python依赖安装完成"
echo ""

# 10. 创建数据库目录
echo "步骤10/17: 数据库配置..."
sudo mkdir -p /opt/stock-analyse/data
sudo chown -R $USER:$USER /opt/stock-analyse/data
echo "✓ 数据库目录创建完成"
echo ""

# 11. 初始化数据库
echo "步骤11/17: 初始化数据库..."
cd /opt/stock-analyse/publish/backend
timeout 30 dotnet StockAnalyse.Api.dll --migrate-database || echo "数据库初始化完成（或已在运行）"
cd /opt/stock-analyse
echo "✓ 数据库初始化完成"
echo ""

# 12. 创建systemd服务
echo "步骤12/17: 创建服务..."

# 后端API服务
sudo tee /etc/systemd/system/stock-backend.service > /dev/null <<EOF
[Unit]
Description=Stock Analyse Backend API
After=network.target

[Service]
Type=simple
User=$USER
WorkingDirectory=/opt/stock-analyse/publish/backend
ExecStart=/usr/bin/dotnet StockAnalyse.Api.dll --urls=http://localhost:5000
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
EOF

# Python数据服务
sudo tee /etc/systemd/system/stock-python.service > /dev/null <<EOF
[Unit]
Description=Stock Analyse Python Data Service
After=network.target

[Service]
Type=simple
User=$USER
WorkingDirectory=/opt/stock-analyse/python-data-service
ExecStart=/usr/bin/python3 stock_data_service.py
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

echo "✓ 服务文件创建完成"
echo ""

# 13. 启动服务
echo "步骤13/17: 启动服务..."
sudo systemctl daemon-reload
sudo systemctl enable stock-backend
sudo systemctl enable stock-python
sudo systemctl start stock-backend
sudo systemctl start stock-python

# 等待服务启动
sleep 5
echo "✓ 服务启动完成"
echo ""

# 14. 防火墙配置
echo "步骤14/17: 防火墙配置..."
sudo firewall-cmd --permanent --add-port=80/tcp
sudo firewall-cmd --permanent --add-port=443/tcp
sudo firewall-cmd --permanent --add-port=5000/tcp
sudo firewall-cmd --permanent --add-port=5001/tcp
sudo firewall-cmd --reload
echo "✓ 防火墙配置完成"
echo ""

# 15. 安装Nginx
echo "步骤15/17: 安装Nginx..."
sudo yum install -y nginx
sudo systemctl enable nginx
echo "✓ Nginx安装完成"
echo ""

# 16. 配置Nginx
echo "步骤16/17: 配置Nginx..."
sudo tee /etc/nginx/conf.d/stock-analyse.conf > /dev/null <<EOF
server {
    listen 80;
    server_name $DOMAIN_NAME www.$DOMAIN_NAME $SERVER_IP;

    # 前端静态文件
    location / {
        root /opt/stock-analyse/frontend/dist;
        index index.html index.htm;
        try_files \$uri \$uri/ /index.html;

        # 缓存配置
        location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg)$ {
            expires 1y;
            add_header Cache-Control "public, immutable";
        }
    }

    # API代理
    location /api/ {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;

        # API超时配置
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }

    # Swagger API文档
    location /swagger/ {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
    }

    # 健康检查
    location /health {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        access_log off;
    }

    # 隐藏nginx版本信息
    server_tokens off;

    # 安全头
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "no-referrer-when-downgrade" always;
}

# Python数据服务代理（可选）
server {
    listen 5001;
    server_name localhost;

    location / {
        proxy_pass http://localhost:5001;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF

echo "✓ Nginx配置完成"
echo ""

# 17. 启动Nginx
echo "步骤17/17: 启动Nginx..."
sudo nginx -t
sudo systemctl start nginx
echo "✓ Nginx启动完成"
echo ""

# ==================== 部署完成 ====================

echo "🎉 部署完成！"
echo ""
echo "=== 访问地址 ==="
echo "前端界面: http://$DOMAIN_NAME 或 http://$SERVER_IP"
echo "API文档: http://$DOMAIN_NAME/swagger 或 http://$SERVER_IP/swagger"
echo "健康检查: http://$DOMAIN_NAME/health"
echo ""

echo "=== 服务状态检查 ==="
echo "后端API服务:"
sudo systemctl status stock-backend --no-pager -l | head -10
echo ""
echo "Python数据服务:"
sudo systemctl status stock-python --no-pager -l | head -10
echo ""
echo "Nginx服务:"
sudo systemctl status nginx --no-pager -l | head -5
echo ""

echo "=== 端口监听检查 ==="
sudo netstat -tlnp | grep -E ':(80|443|5000|5001)' || echo "端口检查命令不可用，请手动检查"
echo ""

echo "=== 部署完成提醒 ==="
echo "⚠️  请记得："
echo "1. 配置域名DNS指向服务器IP: $SERVER_IP"
echo "2. 如需HTTPS，请配置SSL证书"
echo "3. 定期备份数据库文件: /opt/stock-analyse/publish/backend/stockanalyse.db"
echo "4. 监控服务器资源使用情况"
echo ""

echo "=== 常用维护命令 ==="
echo "# 查看服务状态"
echo "sudo systemctl status stock-backend"
echo "sudo systemctl status stock-python"
echo "sudo systemctl status nginx"
echo ""
echo "# 重启服务"
echo "sudo systemctl restart stock-backend"
echo "sudo systemctl restart stock-python"
echo "sudo systemctl restart nginx"
echo ""
echo "# 查看日志"
echo "sudo journalctl -u stock-backend -f"
echo "sudo journalctl -u stock-python -f"
echo "sudo tail -f /var/log/nginx/error.log"
echo ""
echo "# 更新代码"
echo "cd /opt/stock-analyse && git pull"
echo "# 然后重新构建和重启服务"
echo ""

echo "🚀 股票分析系统部署成功完成！"
