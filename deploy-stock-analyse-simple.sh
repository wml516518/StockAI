#!/bin/bash

# ====================================================
# 股票分析系统一键部署脚本 (后端 + Python + 前端)
# 使用前请先配置 deploy-config.sh 文件
# ====================================================

set -Eeuo pipefail

on_error() {
    local exit_code=$?
    local line_no=${1:-}
    echo ""
    echo "❌ 部署失败 (退出码: $exit_code, 行号: ${line_no})"
    echo "请检查上方日志或执行 journalctl -xe 获取更多信息。"
    exit "$exit_code"
}

trap 'on_error ${LINENO}' ERR
trap 'echo -e "\n⚠️  手动中断，退出部署"; exit 130' INT

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_FILE="$SCRIPT_DIR/deploy-config.sh"

if [[ ! -f "$CONFIG_FILE" ]]; then
    echo "❌ 错误: 找不到配置文件 deploy-config.sh"
    echo "请先复制并修改 deploy-config.sh"
    exit 1
fi

source "$CONFIG_FILE"

SERVER_IP=${SERVER_IP:-$(curl -s ifconfig.me || echo "your-server-ip")}
SERVICE_USER=${SERVICE_USER:-$(whoami)}
GIT_BRANCH=${GIT_BRANCH:-master}
PROJECT_ROOT=${PROJECT_ROOT:-/opt/stock-analyse}
BACKEND_PUBLISH_DIR=${BACKEND_PUBLISH_DIR:-"$PROJECT_ROOT/publish/backend"}
FRONTEND_DIST_DIR=${FRONTEND_DIST_DIR:-"$PROJECT_ROOT/frontend/dist"}
PYTHON_SERVICE_DIR=${PYTHON_SERVICE_DIR:-"$PROJECT_ROOT/python-data-service"}
LOG_PATH=${LOG_PATH:-/var/log/stock-analyse}
DATABASE_PATH=${DATABASE_PATH:-"$PROJECT_ROOT/data/stockanalyse.db"}
GITHUB_REPO=${GITHUB_REPO:-""}
DOMAIN_NAME=${DOMAIN_NAME:-"$SERVER_IP"}
BACKEND_PORT=${BACKEND_PORT:-5000}
PYTHON_PORT=${PYTHON_PORT:-5001}
HTTP_PORT=${HTTP_PORT:-80}
HTTPS_PORT=${HTTPS_PORT:-443}
ENABLE_SSL=${ENABLE_SSL:-false}
ENABLE_FIREWALL=${ENABLE_FIREWALL:-true}
SSL_CERT_PATH=${SSL_CERT_PATH:-"/etc/ssl/certs/stock-analyse.crt"}
SSL_KEY_PATH=${SSL_KEY_PATH:-"/etc/ssl/private/stock-analyse.key"}

log_step() {
    echo ""
    echo "==============================="
    echo "▶️  $1"
    echo "==============================="
}

log_info() {
    echo " - $1"
}

log_ok() {
    echo "✓ $1"
}

detect_pkg_manager() {
    if command -v apt-get >/dev/null 2>&1; then
        PKG_MANAGER="apt"
    elif command -v yum >/dev/null 2>&1; then
        PKG_MANAGER="yum"
    else
        echo "❌ 未检测到受支持的包管理器 (apt / yum)。" >&2
        exit 1
    fi
    log_info "使用包管理器: $PKG_MANAGER"
}

pkg_update() {
    case "$PKG_MANAGER" in
        apt)
            sudo apt-get update -y
            ;;
        yum)
            sudo yum update -y
            ;;
    esac
}

pkg_install() {
    if [[ $# -eq 0 ]]; then
        return
    fi
    case "$PKG_MANAGER" in
        apt)
            sudo apt-get install -y "$@"
            ;;
        yum)
            sudo yum install -y "$@"
            ;;
    esac
}

run_as_service_user() {
    if [[ "$(id -un)" == "$SERVICE_USER" ]]; then
        bash -c "$*"
    else
        sudo -u "$SERVICE_USER" bash -c "$*"
    fi
}

install_dotnet() {
    if command -v dotnet >/dev/null 2>&1; then
        log_ok ".NET SDK 已安装: $(dotnet --version)"
        return
    fi

    log_info "安装 .NET SDK 8.0..."
    case "$PKG_MANAGER" in
        apt)
            . /etc/os-release
            local deb_url="https://packages.microsoft.com/config/${ID}/${VERSION_ID}/packages-microsoft-prod.deb"
            if ! curl -fsSL "$deb_url" -o /tmp/packages-microsoft-prod.deb; then
                deb_url="https://packages.microsoft.com/config/${ID}/${VERSION_ID%%.*}/packages-microsoft-prod.deb"
                curl -fsSL "$deb_url" -o /tmp/packages-microsoft-prod.deb
            fi
            sudo dpkg -i /tmp/packages-microsoft-prod.deb
            sudo rm -f /tmp/packages-microsoft-prod.deb
            sudo apt-get update -y
            pkg_install dotnet-sdk-8.0
            ;;
        yum)
            if ! rpm -qa | grep -q packages-microsoft-prod; then
                sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm
            fi
            pkg_install dotnet-sdk-8.0
            ;;
    esac
    log_ok ".NET SDK 安装完成"
}

install_node() {
    if command -v node >/dev/null 2>&1; then
        local node_major
        node_major=$(node -v | sed 's/v//' | cut -d '.' -f1)
        if [[ "$node_major" -ge 18 ]]; then
            log_ok "Node.js 已安装: $(node -v)"
            return
        fi
    fi

    log_info "安装 Node.js 18..."
    case "$PKG_MANAGER" in
        apt)
            curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash -
            pkg_install nodejs
            ;;
        yum)
            curl -fsSL https://rpm.nodesource.com/setup_18.x | sudo bash -
            pkg_install nodejs
            ;;
    esac
    log_ok "Node.js 安装完成: $(node -v)"
}

install_python() {
    if command -v python3 >/dev/null 2>&1 && command -v pip3 >/dev/null 2>&1; then
        log_ok "Python 已安装: $(python3 --version)"
        return
    fi

    log_info "安装 Python3..."
    case "$PKG_MANAGER" in
        apt)
            pkg_install python3 python3-pip python3-venv python3-dev
            ;;
        yum)
            pkg_install python3 python3-pip python3-devel
            ;;
    esac
    log_ok "Python 安装完成: $(python3 --version)"
}

ensure_directories() {
    log_info "创建目录..."
    sudo mkdir -p \
        "$PROJECT_ROOT" \
        "$BACKEND_PUBLISH_DIR" \
        "$FRONTEND_DIST_DIR" \
        "$PYTHON_SERVICE_DIR" \
        "$(dirname "$DATABASE_PATH")" \
        "$LOG_PATH" \
        "/etc/stock-analyse"

    sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$PROJECT_ROOT"
    sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$LOG_PATH" "$(dirname "$DATABASE_PATH")"
    log_ok "目录准备完成"
}

sync_repository() {
    log_step "同步项目代码"
    if [[ -d "$PROJECT_ROOT/.git" ]]; then
        log_info "检测到现有仓库，执行更新..."
        run_as_service_user "cd '$PROJECT_ROOT' && git remote set-url origin '$GITHUB_REPO'"
        run_as_service_user "cd '$PROJECT_ROOT' && git fetch origin '$GIT_BRANCH'"
        run_as_service_user "cd '$PROJECT_ROOT' && git checkout '$GIT_BRANCH'"
        run_as_service_user "cd '$PROJECT_ROOT' && git pull --ff-only origin '$GIT_BRANCH'"
    else
        log_info "首次克隆仓库..."
        sudo rm -rf "$PROJECT_ROOT"
        sudo mkdir -p "$PROJECT_ROOT"
        sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$PROJECT_ROOT"
        run_as_service_user "git clone --branch '$GIT_BRANCH' --single-branch '$GITHUB_REPO' '$PROJECT_ROOT'"
    fi
    log_ok "代码同步完成"
}

build_frontend() {
    if [[ ! -d "$PROJECT_ROOT/frontend" ]]; then
        log_info "未检测到前端目录，跳过前端构建"
        return
    fi

    log_step "构建前端应用"
    run_as_service_user "cd '$PROJECT_ROOT/frontend' && if [[ -f package-lock.json ]]; then npm ci; else npm install; fi"
    run_as_service_user "cd '$PROJECT_ROOT/frontend' && npm run build"

    local dist_source="$PROJECT_ROOT/frontend/dist"
    local dist_target="$FRONTEND_DIST_DIR"

    if [[ "$dist_target" == "$dist_source" ]]; then
        log_info "前端构建输出目录与目标目录相同，跳过复制"
    else
        run_as_service_user "mkdir -p '$dist_target'"
        run_as_service_user "rm -rf '${dist_target:?}/'*"
        run_as_service_user "cp -R '$dist_source/.' '$dist_target/'"
        log_info "构建结果已拷贝至 $dist_target"
    fi

    log_ok "前端构建完成"
}

publish_backend() {
    log_step "发布后端 API"
    run_as_service_user "cd '$PROJECT_ROOT/src/StockAnalyse.Api' && dotnet restore"
    run_as_service_user "cd '$PROJECT_ROOT/src/StockAnalyse.Api' && dotnet publish -c Release -o '$BACKEND_PUBLISH_DIR'"
    log_ok "后端发布完成"
}

configure_python_service() {
    if [[ ! -d "$PYTHON_SERVICE_DIR" ]]; then
        log_info "未检测到 Python 服务目录，跳过配置"
        return
    fi

    if [[ ! -f "$PYTHON_SERVICE_DIR/requirements.txt" ]]; then
        log_info "未找到 requirements.txt，跳过依赖安装"
        return
    fi

    log_step "配置 Python 数据服务"
    run_as_service_user "cd '$PYTHON_SERVICE_DIR' && python3 -m venv .venv"
    run_as_service_user "cd '$PYTHON_SERVICE_DIR' && bash -c 'source .venv/bin/activate && pip install --upgrade pip wheel && pip install -r requirements.txt'"
    log_ok "Python 依赖安装完成"
}

initialize_database() {
    log_step "数据库初始化"
    if [[ -f "$BACKEND_PUBLISH_DIR/StockAnalyse.Api.dll" ]]; then
        run_as_service_user "cd '$BACKEND_PUBLISH_DIR' && timeout 60 dotnet StockAnalyse.Api.dll --migrate-database || true"
        log_ok "数据库迁移完成"
    else
        log_info "未找到后端可执行文件，跳过数据库迁移"
    fi
}

write_environment_files() {
    log_step "生成环境变量文件"
    if [[ ! -f /etc/stock-analyse/backend.env ]]; then
        sudo tee /etc/stock-analyse/backend.env >/dev/null <<'EOF'
# 在此文件添加后端需要的环境变量，格式 KEY=VALUE
# 例如：
# ConnectionStrings__Default=YourConnectionString
EOF
    fi

    if [[ ! -f /etc/stock-analyse/python.env ]]; then
        sudo tee /etc/stock-analyse/python.env >/dev/null <<'EOF'
# 在此文件添加 Python 服务需要的环境变量，格式 KEY=VALUE
EOF
    fi
    log_ok "环境文件检查完成"
}

configure_systemd() {
    log_step "配置 systemd 服务"

    sudo tee /etc/systemd/system/stock-backend.service >/dev/null <<EOF
[Unit]
Description=Stock Analyse Backend API
After=network.target

[Service]
Type=simple
User=$SERVICE_USER
WorkingDirectory=$BACKEND_PUBLISH_DIR
ExecStart=/usr/bin/dotnet StockAnalyse.Api.dll --urls=http://0.0.0.0:$BACKEND_PORT
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
EnvironmentFile=-/etc/stock-analyse/backend.env
SyslogIdentifier=stock-backend

[Install]
WantedBy=multi-user.target
EOF

    local python_exec="$PYTHON_SERVICE_DIR/.venv/bin/python"
    local python_service_enabled="false"

    if [[ ! -x "$python_exec" ]]; then
        python_exec="$(command -v python3 || true)"
    fi

    if [[ -x "$python_exec" && -f "$PYTHON_SERVICE_DIR/stock_data_service.py" ]]; then
        sudo tee /etc/systemd/system/stock-python.service >/dev/null <<EOF
[Unit]
Description=Stock Analyse Python Data Service
After=network.target

[Service]
Type=simple
User=$SERVICE_USER
WorkingDirectory=$PYTHON_SERVICE_DIR
ExecStart=$python_exec stock_data_service.py
Restart=always
RestartSec=10
EnvironmentFile=-/etc/stock-analyse/python.env
SyslogIdentifier=stock-python

[Install]
WantedBy=multi-user.target
EOF
        python_service_enabled="true"
    else
        log_info "未检测到 Python 虚拟环境或 stock_data_service.py，跳过 systemd Python 服务配置"
        sudo rm -f /etc/systemd/system/stock-python.service
        sudo systemctl disable --now stock-python >/dev/null 2>&1 || true
    fi

    sudo systemctl daemon-reload
    sudo systemctl enable stock-backend
    sudo systemctl restart stock-backend || sudo systemctl start stock-backend

    if [[ "$python_service_enabled" == "true" ]]; then
        sudo systemctl enable stock-python
        sudo systemctl restart stock-python || sudo systemctl start stock-python
        log_ok "systemd 服务已启动"
    else
        log_ok "后端 systemd 服务已启动"
    fi
}

configure_firewall() {
    [[ "$ENABLE_FIREWALL" != "true" ]] && return

    log_step "配置防火墙规则"
    if command -v firewall-cmd >/dev/null 2>&1; then
        sudo firewall-cmd --permanent --add-port="${HTTP_PORT}/tcp"
        sudo firewall-cmd --permanent --add-port="${BACKEND_PORT}/tcp"
        sudo firewall-cmd --permanent --add-port="${PYTHON_PORT}/tcp"
        if [[ "$ENABLE_SSL" == "true" ]]; then
            sudo firewall-cmd --permanent --add-port="${HTTPS_PORT}/tcp"
        fi
        sudo firewall-cmd --reload
        log_ok "firewalld 规则已更新"
    elif command -v ufw >/dev/null 2>&1; then
        sudo ufw allow "$HTTP_PORT"/tcp
        sudo ufw allow "$BACKEND_PORT"/tcp
        sudo ufw allow "$PYTHON_PORT"/tcp
        if [[ "$ENABLE_SSL" == "true" ]]; then
            sudo ufw allow "$HTTPS_PORT"/tcp
        fi
        log_ok "ufw 规则已更新"
    else
        log_info "未检测到 firewalld / ufw，跳过防火墙配置"
    fi
}

ensure_nginx_installed() {
    log_step "安装 / 配置 Nginx"
    if ! command -v nginx >/dev/null 2>&1; then
        pkg_install nginx
    fi
    sudo systemctl enable nginx
}

configure_nginx() {
    local nginx_conf="/etc/nginx/conf.d/stock-analyse.conf"

    if [[ "$ENABLE_SSL" == "true" ]]; then
        sudo tee "$nginx_conf" >/dev/null <<EOF
server {
    listen 80;
    server_name $DOMAIN_NAME;
    return 301 https://\$host\$request_uri;
}

server {
    listen 443 ssl http2;
    server_name $DOMAIN_NAME www.$DOMAIN_NAME;

    ssl_certificate $SSL_CERT_PATH;
    ssl_certificate_key $SSL_KEY_PATH;
    ssl_protocols TLSv1.2 TLSv1.3;

    root $FRONTEND_DIST_DIR;
    index index.html;

    location / {
        try_files \$uri \$uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://127.0.0.1:$BACKEND_PORT;
        proxy_http_version 1.1;
        proxy_connect_timeout 600s;
        proxy_send_timeout 600s;
        proxy_read_timeout 600s;
        send_timeout 600s;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location /swagger/ {
        proxy_pass http://127.0.0.1:$BACKEND_PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location /health {
        proxy_pass http://127.0.0.1:$BACKEND_PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        access_log off;
    }

    add_header X-Frame-Options "SAMEORIGIN";
    add_header X-Content-Type-Options "nosniff";
    add_header X-XSS-Protection "1; mode=block";
    add_header Referrer-Policy "no-referrer-when-downgrade";
}
EOF
    else
        sudo tee "$nginx_conf" >/dev/null <<EOF
server {
    listen 80;
    server_name $DOMAIN_NAME www.$DOMAIN_NAME $SERVER_IP;

    root $FRONTEND_DIST_DIR;
    index index.html;

    location / {
        try_files \$uri \$uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://127.0.0.1:$BACKEND_PORT;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location /swagger/ {
        proxy_pass http://127.0.0.1:$BACKEND_PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location /health {
        proxy_pass http://127.0.0.1:$BACKEND_PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        access_log off;
    }

    add_header X-Frame-Options "SAMEORIGIN";
    add_header X-Content-Type-Options "nosniff";
    add_header X-XSS-Protection "1; mode=block";
    add_header Referrer-Policy "no-referrer-when-downgrade";
}
EOF
    fi

    sudo nginx -t
    sudo systemctl restart nginx
    log_ok "Nginx 配置完成"
}

print_summary() {
    echo ""
    echo "🎉 部署完成"
    echo "----------------------------------------"
    echo "仓库地址 : $GITHUB_REPO"
    echo "访问域名 : $DOMAIN_NAME"
    echo "服务器IP : $SERVER_IP"
    echo "后端端口 : $BACKEND_PORT"
    echo "Python端口: $PYTHON_PORT"
    echo ""
    if [[ "$ENABLE_SSL" == "true" ]]; then
        echo "前端: https://$DOMAIN_NAME"
        echo "API : https://$DOMAIN_NAME/api/"
        echo "Swagger: https://$DOMAIN_NAME/swagger"
    else
        echo "前端: http://$DOMAIN_NAME 或 http://$SERVER_IP"
        echo "API : http://$DOMAIN_NAME/api/ 或 http://$SERVER_IP/api/"
        echo "Swagger: http://$DOMAIN_NAME/swagger 或 http://$SERVER_IP/swagger"
    fi
    echo ""
    echo "服务状态:"
    sudo systemctl status stock-backend --no-pager -l | head -n 10 || true
    if systemctl list-unit-files | grep -q '^stock-python.service'; then
        sudo systemctl status stock-python --no-pager -l | head -n 10 || true
    else
        echo "stock-python.service 未配置或未启用"
    fi
    sudo systemctl status nginx --no-pager -l | head -n 5 || true
    echo ""
    echo "✅ 常用维护命令:"
    echo "sudo systemctl restart stock-backend"
    echo "sudo systemctl restart stock-python"
    echo "sudo systemctl restart nginx"
    echo "sudo journalctl -fu stock-backend"
    echo "sudo journalctl -fu stock-python"
}

main() {
    log_step "部署参数确认"
    
    # 验证关键变量
    if [[ -z "$GITHUB_REPO" ]]; then
        echo "❌ 错误: GITHUB_REPO 未配置，请在 deploy-config.sh 中设置"
        exit 1
    fi
    
    log_info "仓库地址 : $GITHUB_REPO"
    log_info "域名     : $DOMAIN_NAME"
    log_info "服务器IP : $SERVER_IP"
    log_info "服务用户 : $SERVICE_USER"
    log_info "SSL启用  : $ENABLE_SSL"

    detect_pkg_manager
    log_step "系统更新与基础依赖安装"
    pkg_update
    if [[ "$PKG_MANAGER" == "apt" ]]; then
        pkg_install curl git unzip rsync ca-certificates gnupg
    else
        pkg_install curl git unzip rsync ca-certificates gnupg2
    fi
    log_ok "基础工具准备完成"

    install_dotnet
    install_node
    install_python
    ensure_directories

    sync_repository
    build_frontend
    publish_backend
    configure_python_service
    initialize_database
    write_environment_files
    configure_systemd
    configure_firewall
    ensure_nginx_installed
    configure_nginx
    print_summary
}

main "$@"

