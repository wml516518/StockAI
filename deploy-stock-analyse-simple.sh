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
GIT_BRANCH=${GIT_BRANCH:-main}
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
    
    # 使用 set +e 临时禁用错误退出，允许代码同步失败时继续执行
    set +e
    
    if [[ -d "$PROJECT_ROOT/.git" ]]; then
        log_info "检测到现有仓库，执行更新..."
        if ! run_as_service_user "cd '$PROJECT_ROOT' && git remote set-url origin '$GITHUB_REPO'" 2>/dev/null; then
            log_info "⚠️  无法更新远程仓库地址，继续尝试..."
        fi
        
        # 尝试获取指定分支，如果失败则尝试检测默认分支
        if ! run_as_service_user "cd '$PROJECT_ROOT' && git ls-remote --heads origin '$GIT_BRANCH'" >/dev/null 2>&1; then
            log_info "分支 '$GIT_BRANCH' 不存在，尝试检测默认分支..."
            local default_branch
            default_branch=$(run_as_service_user "cd '$PROJECT_ROOT' && git ls-remote --symref origin HEAD | grep 'refs/heads/' | sed 's|.*refs/heads/||' | head -1" 2>/dev/null || echo "")
            if [[ -n "$default_branch" ]]; then
                log_info "检测到默认分支: $default_branch，使用该分支"
                GIT_BRANCH="$default_branch"
            else
                # 尝试常见的分支名
                for branch in main master develop; do
                    if run_as_service_user "cd '$PROJECT_ROOT' && git ls-remote --heads origin '$branch'" >/dev/null 2>&1; then
                        log_info "找到分支: $branch，使用该分支"
                        GIT_BRANCH="$branch"
                        break
                    fi
                done
            fi
        fi
        
        if ! run_as_service_user "cd '$PROJECT_ROOT' && git fetch origin '$GIT_BRANCH'" 2>/dev/null; then
            log_info "⚠️  代码拉取失败，跳过代码同步步骤"
            set -e
            return 0
        fi
        
        if ! run_as_service_user "cd '$PROJECT_ROOT' && git checkout '$GIT_BRANCH'" 2>/dev/null; then
            log_info "⚠️  分支切换失败，跳过代码同步步骤"
            set -e
            return 0
        fi
        
        if ! run_as_service_user "cd '$PROJECT_ROOT' && git pull --ff-only origin '$GIT_BRANCH'" 2>/dev/null; then
            log_info "⚠️  代码更新失败，跳过代码同步步骤"
            set -e
            return 0
        fi
        
        log_ok "代码同步完成 (使用分支: $GIT_BRANCH)"
    else
        log_info "首次克隆仓库..."
        
        # 先尝试克隆指定分支，如果失败则尝试检测默认分支
        if ! run_as_service_user "git ls-remote --heads '$GITHUB_REPO' '$GIT_BRANCH'" >/dev/null 2>&1; then
            log_info "分支 '$GIT_BRANCH' 不存在，尝试检测默认分支..."
            local default_branch
            default_branch=$(run_as_service_user "git ls-remote --symref '$GITHUB_REPO' HEAD | grep 'refs/heads/' | sed 's|.*refs/heads/||' | head -1" 2>/dev/null || echo "")
            if [[ -n "$default_branch" ]]; then
                log_info "检测到默认分支: $default_branch，使用该分支"
                GIT_BRANCH="$default_branch"
            else
                # 尝试常见的分支名
                for branch in main master develop; do
                    if run_as_service_user "git ls-remote --heads '$GITHUB_REPO' '$branch'" >/dev/null 2>&1; then
                        log_info "找到分支: $branch，使用该分支"
                        GIT_BRANCH="$branch"
                        break
                    fi
                done
            fi
        fi
        
        sudo rm -rf "$PROJECT_ROOT"
        sudo mkdir -p "$PROJECT_ROOT"
        sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$PROJECT_ROOT"
        
        if ! run_as_service_user "git clone --branch '$GIT_BRANCH' --single-branch '$GITHUB_REPO' '$PROJECT_ROOT'" 2>/dev/null; then
            log_info "⚠️  代码克隆失败，跳过代码同步步骤"
            log_info "提示: 如果项目目录已存在，将使用现有代码继续部署"
            set -e
            return 0
        fi
        
        log_ok "代码同步完成 (使用分支: $GIT_BRANCH)"
    fi
    
    # 恢复错误退出模式
    set -e
}

build_frontend() {
    if [[ ! -d "$PROJECT_ROOT" ]]; then
        log_info "⚠️  项目目录不存在，跳过前端构建"
        return
    fi
    
    if [[ ! -d "$PROJECT_ROOT/frontend" ]]; then
        log_info "未检测到前端目录，跳过前端构建"
        return
    fi

    log_step "构建前端应用"
    
    # 安装依赖
    log_info "安装前端依赖..."
    if ! run_as_service_user "cd '$PROJECT_ROOT/frontend' && if [[ -f package-lock.json ]]; then npm ci; else npm install; fi" 2>&1; then
        log_info "⚠️  前端依赖安装失败，跳过前端构建"
        return
    fi
    
    # 构建前端
    log_info "执行前端构建..."
    if ! run_as_service_user "cd '$PROJECT_ROOT/frontend' && npm run build" 2>&1; then
        log_info "⚠️  前端构建失败，跳过前端构建步骤"
        return
    fi

    local dist_source="$PROJECT_ROOT/frontend/dist"
    local dist_target="$FRONTEND_DIST_DIR"

    # 检查构建输出是否存在
    if [[ ! -d "$dist_source" ]]; then
        log_info "⚠️  前端构建输出目录不存在: $dist_source"
        return
    fi
    
    if [[ ! -f "$dist_source/index.html" ]]; then
        log_info "⚠️  前端构建输出中未找到 index.html"
        log_info "构建输出目录内容:"
        run_as_service_user "ls -la '$dist_source'" 2>&1 || true
        return
    fi

    log_info "前端构建成功，输出目录: $dist_source"
    log_info "目标目录: $dist_target"

    # 确保目标目录存在
    log_info "准备目标目录..."
    sudo mkdir -p "$dist_target"
    sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$dist_target"
    
    # 清空目标目录（保留目录本身）
    log_info "清空目标目录..."
    run_as_service_user "rm -rf '${dist_target:?}/'*" 2>/dev/null || true
    run_as_service_user "rm -rf '${dist_target:?}/'.*" 2>/dev/null || true
    
    # 复制构建结果
    log_info "复制构建结果到目标目录..."
    if run_as_service_user "cp -R '$dist_source/.' '$dist_target/'" 2>&1; then
        log_info "构建结果已拷贝至 $dist_target"
    else
        log_info "⚠️  复制失败，尝试使用 sudo..."
        sudo cp -R "$dist_source/." "$dist_target/"
        sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$dist_target"
    fi
    
    # 验证复制是否成功
    if [[ -f "$dist_target/index.html" ]]; then
        log_ok "前端构建完成，文件已复制到 $dist_target"
        log_info "验证: index.html 存在，文件大小: $(du -h '$dist_target/index.html' | cut -f1)"
    else
        log_info "⚠️  警告: 复制后未找到 index.html，请检查复制过程"
        log_info "目标目录内容:"
        ls -la "$dist_target" 2>&1 || true
    fi
    
    # 设置正确的权限
    log_info "设置文件权限..."
    sudo find "$dist_target" -type d -exec chmod 755 {} \;
    sudo find "$dist_target" -type f -exec chmod 644 {} \;
    sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$dist_target"
}

publish_backend() {
    if [[ ! -d "$PROJECT_ROOT" ]]; then
        log_info "⚠️  项目目录不存在，跳过后端发布"
        return
    fi
    
    if [[ ! -d "$PROJECT_ROOT/src/StockAnalyse.Api" ]]; then
        log_info "⚠️  后端项目目录不存在，跳过后端发布"
        return
    fi
    
    log_step "发布后端 API"
    if ! run_as_service_user "cd '$PROJECT_ROOT/src/StockAnalyse.Api' && dotnet restore" 2>/dev/null; then
        log_info "⚠️  后端依赖恢复失败，跳过后端发布"
        return
    fi
    
    if ! run_as_service_user "cd '$PROJECT_ROOT/src/StockAnalyse.Api' && dotnet publish -c Release -o '$BACKEND_PUBLISH_DIR'" 2>/dev/null; then
        log_info "⚠️  后端发布失败，跳过后端发布步骤"
        return
    fi
    
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
        # 确保端口未被占用（数据库迁移可能会启动临时服务）
        free_port "$BACKEND_PORT"
        
        log_info "执行数据库迁移..."
        # 使用 set +e 允许迁移失败时继续
        set +e
        run_as_service_user "cd '$BACKEND_PUBLISH_DIR' && timeout 60 dotnet StockAnalyse.Api.dll --migrate-database" 2>&1 | head -20 || true
        local migrate_status=$?
        set -e
        
        if [[ $migrate_status -eq 0 ]]; then
            log_ok "数据库迁移完成"
        else
            log_info "⚠️  数据库迁移可能失败，但继续部署流程"
            log_info "提示: 可以稍后手动运行迁移命令"
        fi
        
        # 迁移后再次清理端口，确保没有残留进程
        free_port "$BACKEND_PORT"
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

# 检查并清理占用端口的进程
free_port() {
    local port=$1
    local service_name=${2:-""}
    
    if [[ -z "$port" ]]; then
        return 0
    fi
    
    log_info "检查端口 $port 是否被占用..."
    
    # 停止对应的 systemd 服务
    if [[ -n "$service_name" ]]; then
        if systemctl is-active --quiet "$service_name" 2>/dev/null; then
            log_info "停止 systemd 服务: $service_name"
            sudo systemctl stop "$service_name" 2>/dev/null || true
            sleep 2
        fi
    fi
    
    # 查找占用端口的进程
    local pid=""
    if command -v lsof >/dev/null 2>&1; then
        pid=$(sudo lsof -ti:$port 2>/dev/null | head -1 || echo "")
    elif command -v ss >/dev/null 2>&1; then
        # 使用 ss 查找占用端口的进程
        local ss_output
        ss_output=$(sudo ss -lptn "sport = :$port" 2>/dev/null || echo "")
        if [[ -n "$ss_output" ]]; then
            # 尝试多种方式提取 PID
            pid=$(echo "$ss_output" | grep -oE 'pid=[0-9]+' | cut -d'=' -f2 | head -1 || echo "")
            if [[ -z "$pid" ]]; then
                pid=$(echo "$ss_output" | awk '{print $6}' | grep -oE '[0-9]+' | head -1 || echo "")
            fi
        fi
    elif command -v netstat >/dev/null 2>&1; then
        pid=$(sudo netstat -tlnp 2>/dev/null | grep ":$port " | awk '{print $7}' | cut -d'/' -f1 | head -1 || echo "")
    elif command -v fuser >/dev/null 2>&1; then
        pid=$(sudo fuser $port/tcp 2>/dev/null | awk '{print $1}' | head -1 || echo "")
    fi
    
    if [[ -n "$pid" && "$pid" =~ ^[0-9]+$ ]]; then
        log_info "发现进程 $pid 占用端口 $port，正在终止..."
        sudo kill -9 "$pid" 2>/dev/null || true
        sleep 1
        log_ok "端口 $port 已释放"
    else
        log_info "端口 $port 未被占用"
    fi
}

configure_systemd() {
    log_step "配置 systemd 服务"

    # 清理可能占用端口的进程
    free_port "$BACKEND_PORT" "stock-backend"
    free_port "$PYTHON_PORT" "stock-python"

    # 动态检测 dotnet 路径
    local dotnet_path
    dotnet_path=$(command -v dotnet 2>/dev/null || echo "")
    
    if [[ -z "$dotnet_path" ]]; then
        # 尝试常见路径
        for path in /usr/bin/dotnet /usr/local/bin/dotnet "$HOME/.dotnet/dotnet"; do
            if [[ -x "$path" ]]; then
                dotnet_path="$path"
                break
            fi
        done
    fi
    
    if [[ -z "$dotnet_path" || ! -x "$dotnet_path" ]]; then
        log_info "⚠️  无法找到 dotnet 可执行文件，使用默认路径 /usr/bin/dotnet"
        log_info "提示: 如果服务启动失败，请运行 'which dotnet' 查看实际路径并手动修改服务配置"
        dotnet_path="/usr/bin/dotnet"
    fi
    
    log_info "使用 dotnet 路径: $dotnet_path"
    
    # 验证工作目录和 DLL 文件是否存在
    if [[ ! -d "$BACKEND_PUBLISH_DIR" ]]; then
        log_info "⚠️  后端发布目录不存在: $BACKEND_PUBLISH_DIR"
        log_info "提示: 将尝试创建目录，但请确保后端已正确发布"
        sudo mkdir -p "$BACKEND_PUBLISH_DIR"
        sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$BACKEND_PUBLISH_DIR"
    fi

    sudo tee /etc/systemd/system/stock-backend.service >/dev/null <<EOF
[Unit]
Description=Stock Analyse Backend API
After=network.target

[Service]
Type=simple
User=$SERVICE_USER
WorkingDirectory=$BACKEND_PUBLISH_DIR
ExecStart=$dotnet_path StockAnalyse.Api.dll --urls=http://0.0.0.0:$BACKEND_PORT
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
EnvironmentFile=-/etc/stock-analyse/backend.env
SyslogIdentifier=stock-backend
StandardOutput=journal
StandardError=journal
# 确保服务在后台运行
KillMode=mixed
KillSignal=SIGTERM
TimeoutStopSec=30

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
StandardOutput=journal
StandardError=journal
# 确保服务在后台运行
KillMode=mixed
KillSignal=SIGTERM
TimeoutStopSec=30

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
    
    # 验证服务配置是否正确
    if ! sudo systemctl list-unit-files | grep -q '^stock-backend.service'; then
        log_info "⚠️  后端服务配置可能有问题，请检查日志"
    fi
    
    # 确保服务完全停止
    if systemctl is-active --quiet stock-backend 2>/dev/null; then
        log_info "停止现有后端服务..."
        sudo systemctl stop stock-backend 2>/dev/null || true
        sleep 3
    fi
    
    # 再次检查并清理端口
    free_port "$BACKEND_PORT"
    
    sudo systemctl enable stock-backend
    
    # 检查 DLL 文件是否存在
    if [[ ! -f "$BACKEND_PUBLISH_DIR/StockAnalyse.Api.dll" ]]; then
        log_info "⚠️  警告: 未找到 StockAnalyse.Api.dll 文件"
        log_info "提示: 服务可能无法启动，请确保后端已正确发布"
    fi
    
    # 启动服务
    log_info "启动后端服务..."
    if sudo systemctl start stock-backend 2>/dev/null; then
        sleep 3
        # 等待服务启动，最多等待10秒
        local max_wait=10
        local waited=0
        while [[ $waited -lt $max_wait ]]; do
            if sudo systemctl is-active --quiet stock-backend 2>/dev/null; then
                log_ok "后端 systemd 服务已启动"
                break
            fi
            sleep 1
            waited=$((waited + 1))
        done
        
        if [[ $waited -ge $max_wait ]]; then
            log_info "⚠️  后端服务启动可能有问题，请运行 'sudo systemctl status stock-backend' 查看详情"
            log_info "提示: 检查端口是否被占用: sudo lsof -i:$BACKEND_PORT"
        fi
    else
        log_info "⚠️  后端服务启动失败，请运行 'sudo systemctl status stock-backend' 查看详情"
    fi

    if [[ "$python_service_enabled" == "true" ]]; then
        # 确保 Python 服务完全停止
        if systemctl is-active --quiet stock-python 2>/dev/null; then
            log_info "停止现有 Python 服务..."
            sudo systemctl stop stock-python 2>/dev/null || true
            sleep 2
        fi
        
        # 再次检查并清理端口
        free_port "$PYTHON_PORT"
        
        sudo systemctl enable stock-python
        log_info "启动 Python 服务..."
        
        if sudo systemctl start stock-python 2>/dev/null; then
            sleep 3
            # 等待服务启动，最多等待10秒
            local max_wait=10
            local waited=0
            while [[ $waited -lt $max_wait ]]; do
                if sudo systemctl is-active --quiet stock-python 2>/dev/null; then
                    log_ok "Python systemd 服务已启动"
                    break
                fi
                sleep 1
                waited=$((waited + 1))
            done
            
            if [[ $waited -ge $max_wait ]]; then
                log_info "⚠️  Python 服务启动可能有问题，请运行 'sudo systemctl status stock-python' 查看详情"
                log_info "提示: 检查端口是否被占用: sudo lsof -i:$PYTHON_PORT"
            fi
        else
            log_info "⚠️  Python 服务启动失败，请运行 'sudo systemctl status stock-python' 查看详情"
        fi
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
    
    log_step "配置 Nginx"
    
    # 检查前端目录是否存在
    if [[ ! -d "$FRONTEND_DIST_DIR" ]]; then
        log_info "⚠️  前端目录不存在: $FRONTEND_DIST_DIR"
        log_info "创建前端目录..."
        sudo mkdir -p "$FRONTEND_DIST_DIR"
        sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$FRONTEND_DIST_DIR"
        
        # 创建默认 index.html
        if [[ ! -f "$FRONTEND_DIST_DIR/index.html" ]]; then
            log_info "创建默认 index.html 文件..."
            sudo tee "$FRONTEND_DIST_DIR/index.html" >/dev/null <<'HTML'
<!DOCTYPE html>
<html>
<head>
    <title>Stock Analyse - 部署中</title>
    <meta charset="utf-8">
    <style>
        body { font-family: Arial, sans-serif; text-align: center; padding: 50px; }
        h1 { color: #333; }
        p { color: #666; }
    </style>
</head>
<body>
    <h1>🚀 Stock Analyse</h1>
    <p>前端文件正在部署中，请稍候...</p>
    <p>如果此页面持续显示，请检查前端构建是否完成。</p>
</body>
</html>
HTML
            sudo chown "$SERVICE_USER":"$SERVICE_USER" "$FRONTEND_DIST_DIR/index.html"
        fi
    fi
    
    # 检查 index.html 是否存在
    if [[ ! -f "$FRONTEND_DIST_DIR/index.html" ]]; then
        log_info "⚠️  警告: index.html 不存在，创建默认文件..."
        sudo tee "$FRONTEND_DIST_DIR/index.html" >/dev/null <<'HTML'
<!DOCTYPE html>
<html>
<head>
    <title>Stock Analyse</title>
    <meta charset="utf-8">
</head>
<body>
    <h1>Stock Analyse</h1>
    <p>前端文件未找到，请确保前端已正确构建。</p>
</body>
</html>
HTML
        sudo chown "$SERVICE_USER":"$SERVICE_USER" "$FRONTEND_DIST_DIR/index.html"
    fi
    
    # 设置正确的权限（nginx 用户需要读取权限）
    log_info "设置前端目录权限..."
    sudo chmod -R 755 "$FRONTEND_DIST_DIR"
    sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$FRONTEND_DIST_DIR"
    
    # 获取 nginx 用户（通常是 nginx 或 www-data）
    local nginx_user="nginx"
    if ! id "$nginx_user" &>/dev/null; then
        nginx_user="www-data"
    fi
    
    # 确保 nginx 用户可以访问目录（通过组权限或 ACL）
    if id "$nginx_user" &>/dev/null; then
        log_info "确保 nginx 用户 ($nginx_user) 可以访问前端目录..."
        # 设置目录权限，允许其他用户读取
        sudo find "$FRONTEND_DIST_DIR" -type d -exec chmod 755 {} \;
        sudo find "$FRONTEND_DIST_DIR" -type f -exec chmod 644 {} \;
    fi
    
    log_info "前端目录: $FRONTEND_DIST_DIR"
    log_info "index.html 存在: $([ -f "$FRONTEND_DIST_DIR/index.html" ] && echo "是" || echo "否")"

    if [[ "$ENABLE_SSL" == "true" ]]; then
        sudo tee "$nginx_conf" >/dev/null <<EOF
server {
    listen 80;
    server_name $DOMAIN_NAME;
    return 301 https://\$host\$request_uri;
}

server {
    listen 443 ssl http2;
    server_name $DOMAIN_NAME;

    ssl_certificate $SSL_CERT_PATH;
    ssl_certificate_key $SSL_KEY_PATH;
    ssl_protocols TLSv1.2 TLSv1.3;

    root $FRONTEND_DIST_DIR;
    index index.html;
    
    # 增加超时设置以支持长时间运行的AI分析
    client_body_timeout 600s;
    client_header_timeout 600s;
    keepalive_timeout 600s;

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
        proxy_buffering off;
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
    server_name $DOMAIN_NAME;

    root $FRONTEND_DIST_DIR;
    index index.html;
    
    # 增加超时设置以支持长时间运行的AI分析
    client_body_timeout 600s;
    client_header_timeout 600s;
    keepalive_timeout 600s;

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
        proxy_buffering off;
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

    # 测试 Nginx 配置
    log_info "测试 Nginx 配置..."
    if ! sudo nginx -t 2>&1; then
        log_info "⚠️  Nginx 配置测试失败，请检查配置"
        return 1
    fi
    
    # 重启 Nginx
    log_info "重启 Nginx..."
    sudo systemctl restart nginx
    
    # 等待 Nginx 启动
    sleep 2
    
    # 检查 Nginx 状态
    if sudo systemctl is-active --quiet nginx; then
        log_ok "Nginx 配置完成并已启动"
    else
        log_info "⚠️  Nginx 启动可能有问题，请检查日志: sudo tail -f /var/log/nginx/error.log"
    fi
    
    # 显示诊断信息
    log_info "前端目录诊断:"
    log_info "  目录路径: $FRONTEND_DIST_DIR"
    log_info "  目录存在: $([ -d "$FRONTEND_DIST_DIR" ] && echo "是" || echo "否")"
    log_info "  index.html: $([ -f "$FRONTEND_DIST_DIR/index.html" ] && echo "是" || echo "否")"
    log_info "  目录权限: $(ls -ld "$FRONTEND_DIST_DIR" 2>/dev/null | awk '{print $1, $3, $4}' || echo "无法读取")"
    
    # 检查 SELinux（如果启用）
    if command -v getenforce >/dev/null 2>&1; then
        local selinux_status
        selinux_status=$(getenforce 2>/dev/null || echo "Disabled")
        if [[ "$selinux_status" != "Disabled" ]]; then
            log_info "⚠️  SELinux 已启用 ($selinux_status)，可能需要设置上下文:"
            log_info "  sudo chcon -R -t httpd_sys_content_t '$FRONTEND_DIST_DIR'"
        fi
    fi
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

