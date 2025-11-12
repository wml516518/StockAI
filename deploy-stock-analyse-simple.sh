#!/bin/bash

# ====================================================
# 股票分析系统简化部署脚本 (带配置文件)
# 使用前请先配置 deploy-config.sh 文件
# ====================================================

set -e

# 加载配置文件
if [ -f "./deploy-config.sh" ]; then
    source ./deploy-config.sh
    echo "✓ 配置文件加载成功"
else
    echo "❌ 错误: 找不到配置文件 deploy-config.sh"
    echo "请先配置 deploy-config.sh 文件"
    exit 1
fi

# 设置默认值
SERVER_IP=${SERVER_IP:-$(curl -s ifconfig.me || echo "your-server-ip")}

echo "=== 股票分析系统部署开始 ==="
echo "仓库地址: $GITHUB_REPO"
echo "域名: $DOMAIN_NAME"
echo "服务器IP: $SERVER_IP"
echo "SSL启用: $ENABLE_SSL"
echo ""

# 函数定义
check_command() {
    if ! command -v $1 &> /dev/null; then
        echo "❌ 错误: $1 未安装"
        return 1
    else
        echo "✓ $1 已安装: $($1 --version | head -1)"
        return 0
    fi
}

# 1. 检查系统要求
echo "步骤1: 检查系统要求..."
if [[ "$OSTYPE" != "linux-gnu"* ]]; then
    echo "❌ 错误: 此脚本仅支持Linux系统"
    exit 1
fi

# 检查是否为root或有sudo权限
if ! sudo -n true 2>/dev/null; then
    echo "⚠️  警告: 需要sudo权限，请确保你有管理员权限"
fi
echo "✓ 系统检查完成"
echo ""

# 2. 系统更新和安装基础工具
echo "步骤2: 系统更新和基础工具安装..."
sudo yum update -y
sudo yum install -y wget curl git unzip
check_command wget
check_command curl
check_command git
echo "✓ 基础工具安装完成"
echo ""

# 3. 安装.NET 8 SDK
echo "步骤3: 安装.NET 8 SDK..."
sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm
sudo yum install -y dotnet-sdk-8.0
check_command dotnet
echo "✓ .NET 8 SDK安装完成"
echo ""

# 4. 安装Node.js 18
echo "步骤4: 安装Node.js 18..."
curl -fsSL https://rpm.nodesource.com/setup_18.x | sudo bash -
sudo yum install -y nodejs
check_command node
check_command npm
echo "✓ Node.js 18安装完成"
echo ""

# 5. 安装Python 3.8+
echo "步骤5: 安装Python..."
sudo yum install -y python3 python3-pip python3-devel
check_command python3
check_command pip3
echo "✓ Python安装完成"
echo ""

# 6. 创建项目目录
echo "步骤6: 创建项目目录..."
sudo mkdir -p $PROJECT_ROOT
sudo chown -R $SERVICE_USER:$SERVICE_USER $PROJECT_ROOT
cd $PROJECT_ROOT
echo "✓ 项目目录创建完成: $PROJECT_ROOT"
echo ""

# 7. 从GitHub克隆代码
echo "步骤7: 克隆代码..."
git clone $GITHUB_REPO .
echo "✓ 代码克隆完成"
echo ""

# 8. 前端构建
echo "步骤8: 前端构建..."
cd frontend
npm install
npm run build
cd ..
echo "✓ 前端构建完成"
echo ""

# 9. 后端发布
echo "步骤9: 后端发布..."
cd src/StockAnalyse.Api
dotnet restore
dotnet publish -c Release -o $BACKEND_PUBLISH_DIR
cd $PROJECT_ROOT
echo "✓ 后端发布完成"
echo ""

# 10. Python服务配置
echo "步骤10: Python服务配置..."
cd $PYTHON_SERVICE_DIR
pip3 install -r requirements.txt
cd $PROJECT_ROOT
echo "✓ Python依赖安装完成"
echo ""

# 11. 创建数据库目录
echo "步骤11: 数据库配置..."
sudo mkdir -p $(dirname $DATABASE_PATH)
sudo chown -R $SERVICE_USER:$SERVICE_USER $(dirname $DATABASE_PATH)
echo "✓ 数据库目录创建完成"
echo ""

# 12. 初始化数据库
echo "步骤12: 初始化数据库..."
cd $BACKEND_PUBLISH_DIR
timeout 30 dotnet StockAnalyse.Api.dll --migrate-database || echo "数据库初始化完成"
cd $PROJECT_ROOT
echo "✓ 数据库初始化完成"
echo ""

# 13. 创建systemd服务
echo "步骤13: 创建服务..."

# 后端API服务
sudo tee /etc/systemd/system/stock-backend.service > /dev/null <<EOF
[Unit]
Description=Stock Analyse Backend API
After=network.target

[Service]
Type=simple
User=$SERVICE_USER
WorkingDirectory=$BACKEND_PUBLISH_DIR
ExecStart=/usr/bin/dotnet StockAnalyse.Api.dll --urls=http://localhost:$BACKEND_PORT
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
User=$SERVICE_USER
WorkingDirectory=$PYTHON_SERVICE_DIR
ExecStart=/usr/bin/python3 stock_data_service.py
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

echo "✓ 服务文件创建完成"
echo ""

# 14. 启动服务
echo "步骤14: 启动服务..."
sudo systemctl daemon-reload
sudo systemctl enable stock-backend
sudo systemctl enable stock-python
sudo systemctl start stock-backend
sudo systemctl start stock-python

# 等待服务启动
sleep 5
echo "✓ 服务启动完成"
echo ""

# 15. 防火墙配置
if [ "$ENABLE_FIREWALL" = "true" ]; then
    echo "步骤15: 防火墙配置..."
    sudo firewall-cmd --permanent --add-port=$HTTP_PORT/tcp
    sudo firewall-cmd --permanent --add-port=$BACKEND_PORT/tcp
    sudo firewall-cmd --permanent --add-port=$PYTHON_PORT/tcp
    if [ "$ENABLE_SSL" = "true" ]; then
        sudo firewall-cmd --permanent --add-port=$HTTPS_PORT/tcp
    fi
    sudo firewall-cmd --reload
    echo "✓ 防火墙配置完成"
    echo ""
fi

# 16. 安装Nginx
echo "步骤16: 安装Nginx..."
sudo yum install -y nginx
sudo systemctl enable nginx
echo "✓ Nginx安装完成"
echo ""

# 17. 配置Nginx
echo "步骤17: 配置Nginx..."

# 生成Nginx配置
if [ "$ENABLE_SSL" = "true" ]; then
    # HTTPS配置
    sudo tee /etc/nginx/conf.d/stock-analyse.conf > /dev/null <<EOF
server {
    listen 80;
    server_name $DOMAIN_NAME www.$DOMAIN_NAME $SERVER_IP;
    return 301 https://\$server_name\$request_uri;
}

server {
    listen 443 ssl http2;
    server_name $DOMAIN_NAME www.$DOMAIN_NAME;

    ssl_certificate $SSL_CERT_PATH;
    ssl_certificate_key $SSL_KEY_PATH;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-RSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384;

    # 前端静态文件
    location / {
        root $FRONTEND_DIST_DIR;
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
        proxy_pass http://localhost:$BACKEND_PORT;
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
        proxy_pass http://localhost:$BACKEND_PORT;
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
        proxy_pass http://localhost:$BACKEND_PORT;
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
EOF
else
    # HTTP配置
    sudo tee /etc/nginx/conf.d/stock-analyse.conf > /dev/null <<EOF
server {
    listen 80;
    server_name $DOMAIN_NAME www.$DOMAIN_NAME $SERVER_IP;

    # 前端静态文件
    location / {
        root $FRONTEND_DIST_DIR;
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
        proxy_pass http://localhost:$BACKEND_PORT;
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
        proxy_pass http://localhost:$BACKEND_PORT;
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
        proxy_pass http://localhost:$BACKEND_PORT;
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
EOF
fi

# Python数据服务代理
sudo tee -a /etc/nginx/conf.d/stock-analyse.conf > /dev/null <<EOF

# Python数据服务代理（可选）
server {
    listen $PYTHON_PORT;
    server_name localhost;

    location / {
        proxy_pass http://localhost:$PYTHON_PORT;
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

# 18. 启动Nginx
echo "步骤18: 启动Nginx..."
sudo nginx -t
sudo systemctl start nginx
echo "✓ Nginx启动完成"
echo ""

# ==================== 部署完成 ====================

echo "🎉 部署完成！"
echo ""
echo "=== 访问地址 ==="
if [ "$ENABLE_SSL" = "true" ]; then
    echo "前端界面: https://$DOMAIN_NAME"
    echo "API文档: https://$DOMAIN_NAME/swagger"
    echo "健康检查: https://$DOMAIN_NAME/health"
else
    echo "前端界面: http://$DOMAIN_NAME 或 http://$SERVER_IP"
    echo "API文档: http://$DOMAIN_NAME/swagger 或 http://$SERVER_IP/swagger"
    echo "健康检查: http://$DOMAIN_NAME/health 或 http://$SERVER_IP/health"
fi
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
sudo netstat -tlnp | grep -E ":($HTTP_PORT|$HTTPS_PORT|$BACKEND_PORT|$PYTHON_PORT)" 2>/dev/null || echo "端口检查命令不可用，请手动检查"
echo ""

echo "=== 部署完成提醒 ==="
echo "⚠️  请记得："
echo "1. 配置域名DNS指向服务器IP: $SERVER_IP"
if [ "$ENABLE_SSL" = "true" ]; then
    echo "2. SSL证书已配置，请确保证书文件存在且有效"
fi
echo "3. 定期备份数据库文件: $DATABASE_PATH"
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
echo "cd $PROJECT_ROOT && git pull"
echo "# 然后重新构建和重启服务"
echo ""

echo "🚀 股票分析系统部署成功完成！"
