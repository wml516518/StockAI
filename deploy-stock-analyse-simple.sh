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

# 检查并修复 Windows 换行符问题（CRLF -> LF）
# 优先使用 sed，因为它在 Linux 系统上几乎总是可用
if command -v sed >/dev/null 2>&1; then
    # 使用 sed 删除行尾的 \r 字符（更可靠的方法）
    sed -i 's/\r$//' "$CONFIG_FILE" 2>/dev/null || {
        # 如果 sed -i 失败（某些系统不支持），使用临时文件
        sed 's/\r$//' "$CONFIG_FILE" > "$CONFIG_FILE.tmp" 2>/dev/null && \
        mv "$CONFIG_FILE.tmp" "$CONFIG_FILE" 2>/dev/null || true
    }
elif command -v dos2unix >/dev/null 2>&1; then
    # 使用 dos2unix 工具转换（如果可用）
    dos2unix "$CONFIG_FILE" 2>/dev/null || true
elif command -v tr >/dev/null 2>&1; then
    # 使用 tr 删除所有 \r 字符
    tr -d '\r' < "$CONFIG_FILE" > "$CONFIG_FILE.tmp" 2>/dev/null && \
    mv "$CONFIG_FILE.tmp" "$CONFIG_FILE" 2>/dev/null || true
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
    
    # 检查 Git 是否安装
    if ! command -v git >/dev/null 2>&1; then
        log_info "⚠️  Git 未安装，跳过代码同步步骤"
        log_info "提示: 请先安装 Git: sudo apt-get install git 或 sudo yum install git"
        set -e
        return 0
    fi
    
    # 检查仓库地址是否配置
    if [[ -z "$GITHUB_REPO" ]]; then
        log_info "⚠️  GITHUB_REPO 未配置，跳过代码同步步骤"
        set -e
        return 0
    fi
    
    # 测试网络连接和仓库可访问性
    log_info "检查仓库可访问性: $GITHUB_REPO"
    local repo_check_output
    repo_check_output=$(run_as_service_user "git ls-remote '$GITHUB_REPO' HEAD" 2>&1)
    local repo_check_status=$?
    
    if [[ $repo_check_status -ne 0 ]]; then
        log_info "⚠️  无法访问仓库，错误信息:"
        echo "$repo_check_output" | head -5 | sed 's/^/   /'
        log_info "可能的原因:"
        log_info "  1. 网络连接问题"
        log_info "  2. 仓库地址错误或不存在"
        log_info "  3. 需要身份验证（SSH密钥或访问令牌）"
        log_info "  4. 防火墙阻止了 Git 连接"
        log_info "提示: 将使用现有代码继续部署（如果存在）"
        set -e
        return 0
    fi
    
    if [[ -d "$PROJECT_ROOT/.git" ]]; then
        log_info "检测到现有仓库，执行更新..."
        
        # 检查当前仓库的远程地址
        local current_remote
        current_remote=$(run_as_service_user "cd '$PROJECT_ROOT' && git remote get-url origin 2>/dev/null" || echo "")
        log_info "当前远程地址: ${current_remote:-未设置}"
        
        if ! run_as_service_user "cd '$PROJECT_ROOT' && git remote set-url origin '$GITHUB_REPO'" 2>&1; then
            log_info "⚠️  无法更新远程仓库地址，继续尝试..."
        fi
        
        # 强制刷新远程引用，清除可能的缓存
        log_info "刷新远程引用（清除缓存）..."
        
        # 清除 Git 引用缓存（如果存在）
        if [[ -d "$PROJECT_ROOT/.git/refs/remotes/origin" ]]; then
            log_info "清除本地远程引用缓存..."
            run_as_service_user "cd '$PROJECT_ROOT' && rm -rf .git/refs/remotes/origin/*" 2>&1 || true
        fi
        
        # 更新远程引用
        run_as_service_user "cd '$PROJECT_ROOT' && git remote update origin --prune" 2>&1 || true
        run_as_service_user "cd '$PROJECT_ROOT' && git fetch origin --prune" 2>&1 || true
        
        # 如果是 gitclone.com 镜像，可能需要额外处理
        if [[ "$GITHUB_REPO" == *"gitclone.com"* ]]; then
            log_info "检测到 gitclone.com 镜像，清除可能的镜像缓存..."
            # 清除可能的 DNS 缓存（如果系统支持）
            if command -v flushdns >/dev/null 2>&1; then
                sudo flushdns 2>/dev/null || true
            fi
        fi
        
        # 尝试获取指定分支，如果失败则尝试检测默认分支
        log_info "检查分支 '$GIT_BRANCH' 是否存在..."
        
        # 先列出所有远程分支，用于诊断（使用 --no-cache 确保获取最新信息）
        log_info "列出所有远程分支（强制刷新）..."
        local all_branches_output
        # 清除可能的 DNS 或 Git 缓存，强制重新连接
        all_branches_output=$(run_as_service_user "cd '$PROJECT_ROOT' && GIT_TERMINAL_PROMPT=0 git ls-remote --heads origin" 2>&1)
        local all_branches_status=$?
        
        if [[ $all_branches_status -eq 0 ]] && [[ -n "$all_branches_output" ]]; then
            log_info "远程分支列表（原始输出）:"
            echo "$all_branches_output" | head -20 | sed 's/^/   /'
            log_info "远程分支列表（仅分支名）:"
            echo "$all_branches_output" | sed 's|.*refs/heads/||' | sed 's/^/   /' | head -10
        else
            log_info "⚠️  无法列出远程分支，错误信息:"
            echo "$all_branches_output" | head -10 | sed 's/^/   /'
        fi
        
        # 检查指定分支是否存在（强制刷新）
        log_info "检查分支 '$GIT_BRANCH' 是否存在（强制刷新远程引用）..."
        local branch_check_output
        branch_check_output=$(run_as_service_user "cd '$PROJECT_ROOT' && GIT_TERMINAL_PROMPT=0 git ls-remote --heads origin '$GIT_BRANCH'" 2>&1)
        local branch_check_status=$?
        
        if [[ $branch_check_status -eq 0 ]] && [[ -n "$branch_check_output" ]]; then
            log_ok "分支 '$GIT_BRANCH' 存在，使用该分支"
            log_info "分支检查原始输出:"
            echo "$branch_check_output" | sed 's/^/   /'
        else
            log_info "分支 '$GIT_BRANCH' 检查结果:"
            if [[ $branch_check_status -ne 0 ]]; then
                log_info "  命令执行失败 (退出码: $branch_check_status)"
                echo "$branch_check_output" | head -10 | sed 's/^/   /'
            else
                log_info "  分支不存在或输出为空"
                log_info "  原始输出: $branch_check_output"
            fi
            
            log_info "尝试检测默认分支（强制刷新）..."
            local default_branch
            # 使用更精确的方法提取分支名，避免包含 HEAD
            local symref_output
            symref_output=$(run_as_service_user "cd '$PROJECT_ROOT' && GIT_TERMINAL_PROMPT=0 git ls-remote --symref origin HEAD" 2>&1)
            log_info "symref 输出: $symref_output"
            
            default_branch=$(echo "$symref_output" | grep 'refs/heads/' | sed -E 's|.*refs/heads/([^[:space:]]+).*|\1|' | head -1)
            # 如果提取失败，尝试使用 awk
            if [[ -z "$default_branch" ]] || [[ "$default_branch" == *"HEAD"* ]]; then
                default_branch=$(echo "$symref_output" | awk '/refs\/heads\// {match($0, /refs\/heads\/([^[:space:]]+)/, arr); if (arr[1] != "") {print arr[1]; exit}}')
            fi
            # 清理分支名，移除末尾的 "HEAD" 和空格
            default_branch=$(echo "$default_branch" | sed 's/[[:space:]]*HEAD[[:space:]]*$//' | sed 's/^[[:space:]]*//' | sed 's/[[:space:]]*$//' | head -1)
            
            if [[ -n "$default_branch" ]] && [[ "$default_branch" != "HEAD" ]]; then
                log_info "检测到默认分支: $default_branch，使用该分支"
                GIT_BRANCH="$default_branch"
            else
                log_info "无法从 symref 检测默认分支，尝试直接检查常见分支..."
                # 尝试常见的分支名（强制刷新）
                for branch in main master develop; do
                    branch_check_output=$(run_as_service_user "cd '$PROJECT_ROOT' && GIT_TERMINAL_PROMPT=0 git ls-remote --heads origin '$branch'" 2>&1)
                    if [[ $? -eq 0 ]] && [[ -n "$branch_check_output" ]]; then
                        log_info "找到分支: $branch，使用该分支"
                        GIT_BRANCH="$branch"
                        break
                    fi
                done
            fi
        fi
        
        log_info "使用分支: $GIT_BRANCH"
        
        # 强制同步到远程分支（忽略本地未提交的更改）
        log_info "强制同步到远程分支 '$GIT_BRANCH'..."
        
        # 1. 获取最新远程代码
        log_info "获取远程更新..."
        run_as_service_user "cd '$PROJECT_ROOT' && git fetch origin '$GIT_BRANCH'" 2>&1 || {
            log_info "⚠️  获取远程更新失败，继续尝试重置..."
        }
        
        # 2. 切换到目标分支（如果不在该分支上）
        log_info "切换到分支 '$GIT_BRANCH'..."
        run_as_service_user "cd '$PROJECT_ROOT' && git checkout '$GIT_BRANCH'" 2>&1 || {
            log_info "⚠️  分支切换失败，继续尝试重置..."
        }
        
        # 3. 清理工作区（丢弃所有本地更改）
        log_info "清理工作区（丢弃本地未提交的更改）..."
        run_as_service_user "cd '$PROJECT_ROOT' && git clean -fd" 2>&1 || true
        run_as_service_user "cd '$PROJECT_ROOT' && git reset --hard HEAD" 2>&1 || true
        
        # 4. 强制重置到远程分支（确保与远程完全一致）
        log_info "强制重置到远程分支 origin/'$GIT_BRANCH'..."
        local reset_output
        reset_output=$(run_as_service_user "cd '$PROJECT_ROOT' && git reset --hard origin/'$GIT_BRANCH'" 2>&1) || true
        local reset_status=$?
        
        if [[ $reset_status -eq 0 ]] && [[ -z "$(echo "$reset_output" | grep -i 'error\|fatal')" ]]; then
            log_ok "代码已强制同步到远程分支 '$GIT_BRANCH'"
            # 显示重置信息（如果有）
            if [[ -n "$reset_output" ]]; then
                echo "$reset_output" | grep -v "^$" | head -3 | sed 's/^/   /' || true
            fi
        else
            # 即使有错误也尝试继续，因为 reset --hard 通常能成功
            if echo "$reset_output" | grep -q "HEAD is now at"; then
                log_ok "代码已强制同步到远程分支 '$GIT_BRANCH'"
                echo "$reset_output" | grep "HEAD is now at" | sed 's/^/   /' || true
            else
                log_info "⚠️  重置过程中可能有警告，但继续部署..."
                if [[ -n "$reset_output" ]]; then
                    echo "$reset_output" | head -5 | sed 's/^/   /' || true
                fi
            fi
        fi
        
        log_ok "代码同步完成 (使用分支: $GIT_BRANCH)"
    else
        log_info "首次克隆仓库..."
        
        # 先尝试克隆指定分支，如果失败则尝试检测默认分支
        log_info "检查分支 '$GIT_BRANCH' 是否存在..."
        
        # 先列出所有远程分支，用于诊断（强制刷新）
        log_info "列出所有远程分支（强制刷新）..."
        local all_branches_output
        all_branches_output=$(run_as_service_user "GIT_TERMINAL_PROMPT=0 git ls-remote --heads '$GITHUB_REPO'" 2>&1)
        local all_branches_status=$?
        
        if [[ $all_branches_status -eq 0 ]] && [[ -n "$all_branches_output" ]]; then
            log_info "远程分支列表（原始输出）:"
            echo "$all_branches_output" | head -20 | sed 's/^/   /'
            log_info "远程分支列表（仅分支名）:"
            echo "$all_branches_output" | sed 's|.*refs/heads/||' | sed 's/^/   /' | head -10
        else
            log_info "⚠️  无法列出远程分支，错误信息:"
            echo "$all_branches_output" | head -10 | sed 's/^/   /'
        fi
        
        # 检查指定分支是否存在（强制刷新）
        log_info "检查分支 '$GIT_BRANCH' 是否存在（强制刷新远程引用）..."
        local branch_check_output
        branch_check_output=$(run_as_service_user "GIT_TERMINAL_PROMPT=0 git ls-remote --heads '$GITHUB_REPO' '$GIT_BRANCH'" 2>&1)
        local branch_check_status=$?
        
        if [[ $branch_check_status -eq 0 ]] && [[ -n "$branch_check_output" ]]; then
            log_ok "分支 '$GIT_BRANCH' 存在，使用该分支"
            log_info "分支检查原始输出:"
            echo "$branch_check_output" | sed 's/^/   /'
        else
            log_info "分支 '$GIT_BRANCH' 检查结果:"
            if [[ $branch_check_status -ne 0 ]]; then
                log_info "  命令执行失败 (退出码: $branch_check_status)"
                echo "$branch_check_output" | head -10 | sed 's/^/   /'
            else
                log_info "  分支不存在或输出为空"
                log_info "  原始输出: $branch_check_output"
            fi
            
            log_info "尝试检测默认分支（强制刷新）..."
            local default_branch
            # 使用更精确的方法提取分支名，避免包含 HEAD
            local symref_output
            symref_output=$(run_as_service_user "GIT_TERMINAL_PROMPT=0 git ls-remote --symref '$GITHUB_REPO' HEAD" 2>&1)
            log_info "symref 输出: $symref_output"
            
            default_branch=$(echo "$symref_output" | grep 'refs/heads/' | sed -E 's|.*refs/heads/([^[:space:]]+).*|\1|' | head -1)
            # 如果提取失败，尝试使用 awk
            if [[ -z "$default_branch" ]] || [[ "$default_branch" == *"HEAD"* ]]; then
                default_branch=$(echo "$symref_output" | awk '/refs\/heads\// {match($0, /refs\/heads\/([^[:space:]]+)/, arr); if (arr[1] != "") {print arr[1]; exit}}')
            fi
            # 清理分支名，移除末尾的 "HEAD" 和空格
            default_branch=$(echo "$default_branch" | sed 's/[[:space:]]*HEAD[[:space:]]*$//' | sed 's/^[[:space:]]*//' | sed 's/[[:space:]]*$//' | head -1)
            
            if [[ -n "$default_branch" ]] && [[ "$default_branch" != "HEAD" ]]; then
                log_info "检测到默认分支: $default_branch，使用该分支"
                GIT_BRANCH="$default_branch"
            else
                log_info "无法从 symref 检测默认分支，尝试直接检查常见分支..."
                # 尝试常见的分支名（强制刷新）
                for branch in main master develop; do
                    branch_check_output=$(run_as_service_user "GIT_TERMINAL_PROMPT=0 git ls-remote --heads '$GITHUB_REPO' '$branch'" 2>&1)
                    if [[ $? -eq 0 ]] && [[ -n "$branch_check_output" ]]; then
                        log_info "找到分支: $branch，使用该分支"
                        GIT_BRANCH="$branch"
                        break
                    fi
                done
            fi
        fi
        
        log_info "使用分支: $GIT_BRANCH"
        
        # 如果目录存在但非 Git 仓库，备份并删除
        if [[ -d "$PROJECT_ROOT" ]] && [[ ! -d "$PROJECT_ROOT/.git" ]]; then
            log_info "项目目录存在但不是 Git 仓库，备份现有目录..."
            local backup_dir="${PROJECT_ROOT}.backup.$(date +%Y%m%d_%H%M%S)"
            sudo mv "$PROJECT_ROOT" "$backup_dir" 2>/dev/null || true
            log_info "备份目录: $backup_dir"
        else
            sudo rm -rf "$PROJECT_ROOT"
        fi
        
        sudo mkdir -p "$PROJECT_ROOT"
        sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$PROJECT_ROOT"
        
        log_info "克隆仓库到: $PROJECT_ROOT"
        local clone_output
        clone_output=$(run_as_service_user "git clone --branch '$GIT_BRANCH' --single-branch '$GITHUB_REPO' '$PROJECT_ROOT'" 2>&1)
        local clone_status=$?
        
        if [[ $clone_status -ne 0 ]]; then
            log_info "⚠️  代码克隆失败，错误信息:"
            echo "$clone_output" | head -10 | sed 's/^/   /'
            log_info "可能的原因:"
            log_info "  1. 网络连接问题"
            log_info "  2. 仓库地址错误"
            log_info "  3. 权限不足（需要 SSH 密钥或访问令牌）"
            log_info "  4. 磁盘空间不足"
            log_info "提示: 如果项目目录已存在，将使用现有代码继续部署"
            set -e
            return 0
        fi
        
        log_ok "代码同步完成 (使用分支: $GIT_BRANCH)"
    fi
    
    # 显示当前提交信息
    if [[ -d "$PROJECT_ROOT/.git" ]]; then
        local current_commit
        current_commit=$(run_as_service_user "cd '$PROJECT_ROOT' && git rev-parse --short HEAD 2>/dev/null" || echo "未知")
        local current_branch
        current_branch=$(run_as_service_user "cd '$PROJECT_ROOT' && git branch --show-current 2>/dev/null" || echo "未知")
        log_info "当前分支: $current_branch, 提交: $current_commit"
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
    
    # 检查 Node.js 和 npm
    if ! command -v node >/dev/null 2>&1; then
        log_info "⚠️  Node.js 未安装，跳过前端构建"
        return
    fi
    
    if ! command -v npm >/dev/null 2>&1; then
        log_info "⚠️  npm 未安装，跳过前端构建"
        return
    fi
    
    log_info "Node.js 版本: $(node -v)"
    log_info "npm 版本: $(npm -v)"
    
    # 检查 package.json 是否存在
    if [[ ! -f "$PROJECT_ROOT/frontend/package.json" ]]; then
        log_info "⚠️  未找到 package.json，跳过前端构建"
        log_info "前端目录内容:"
        run_as_service_user "ls -la '$PROJECT_ROOT/frontend'" 2>&1 | head -20 || true
        return
    fi
    
    # 检查 package.json 中是否有 build 脚本
    if ! run_as_service_user "cd '$PROJECT_ROOT/frontend' && grep -q '\"build\"' package.json" 2>/dev/null; then
        log_info "⚠️  package.json 中未找到 build 脚本，跳过前端构建"
        log_info "package.json 中的脚本:"
        run_as_service_user "cd '$PROJECT_ROOT/frontend' && grep -A 5 '\"scripts\"' package.json" 2>&1 || true
        return
    fi
    
    # 安装依赖
    log_info "安装前端依赖..."
    log_info "工作目录: $PROJECT_ROOT/frontend"
    
    local install_output
    install_output=$(run_as_service_user "cd '$PROJECT_ROOT/frontend' && if [[ -f package-lock.json ]]; then npm ci; else npm install; fi" 2>&1)
    local install_status=$?
    
    if [[ $install_status -ne 0 ]]; then
        log_info "⚠️  前端依赖安装失败，错误信息:"
        echo "$install_output" | tail -20 | sed 's/^/   /'
        log_info "可能的原因:"
        log_info "  1. 网络连接问题（npm 源访问失败）"
        log_info "  2. package.json 或 package-lock.json 格式错误"
        log_info "  3. 磁盘空间不足"
        log_info "  4. 权限问题"
        log_info "提示: 可以手动进入前端目录执行 npm install 查看详细错误"
        return
    fi
    
    log_ok "前端依赖安装完成"
    
    # 构建前端
    log_info "执行前端构建..."
    log_info "构建命令: npm run build"
    
    local build_output
    build_output=$(run_as_service_user "cd '$PROJECT_ROOT/frontend' && npm run build" 2>&1)
    local build_status=$?
    
    if [[ $build_status -ne 0 ]]; then
        log_info "⚠️  前端构建失败，错误信息:"
        echo "$build_output" | tail -30 | sed 's/^/   /'
        log_info "可能的原因:"
        log_info "  1. 代码编译错误"
        log_info "  2. 依赖版本不兼容"
        log_info "  3. 构建配置错误"
        log_info "  4. 内存不足"
        log_info "提示: 可以手动进入前端目录执行 npm run build 查看详细错误"
        return
    fi
    
    # 显示构建输出的最后几行（通常包含成功信息）
    log_info "构建输出摘要:"
    echo "$build_output" | tail -10 | sed 's/^/   /'

    # 动态检测构建输出目录
    log_info "检测构建输出目录..."
    local dist_source=""
    
    # 方法1: 从 vite.config.js 读取 outDir 配置
    if [[ -f "$PROJECT_ROOT/frontend/vite.config.js" ]]; then
        log_info "读取 vite.config.js 配置..."
        # 尝试提取 outDir 配置（支持相对路径和绝对路径）
        local outdir_config
        outdir_config=$(run_as_service_user "cd '$PROJECT_ROOT/frontend' && grep -oE \"outDir:\\s*['\\\"]([^'\\\"]+)['\\\"]\" vite.config.js 2>/dev/null | head -1 | sed -E \"s/outDir:\\s*['\\\"]([^'\\\"]+)['\\\"]/\\1/\" || echo ''" || echo "")
        
        if [[ -n "$outdir_config" ]]; then
            log_info "从 vite.config.js 检测到输出目录配置: $outdir_config"
            # 处理相对路径（相对于 frontend 目录）
            if [[ "$outdir_config" == ../* ]]; then
                # 相对路径，需要解析
                local resolved_path
                resolved_path=$(run_as_service_user "cd '$PROJECT_ROOT/frontend' && realpath '$outdir_config' 2>/dev/null || echo ''" || echo "")
                if [[ -n "$resolved_path" ]] && [[ -d "$resolved_path" ]]; then
                    dist_source="$resolved_path"
                    log_info "解析后的输出目录: $dist_source"
                else
                    # 手动构建路径（处理 ../src/StockAnalyse.Api/wwwroot）
                    dist_source="$PROJECT_ROOT/${outdir_config#../}"
                    log_info "手动构建的输出目录: $dist_source"
                fi
            else
                # 绝对路径或相对于 frontend 的路径
                if [[ "$outdir_config" == /* ]]; then
                    dist_source="$outdir_config"
                else
                    dist_source="$PROJECT_ROOT/frontend/$outdir_config"
                fi
            fi
        fi
    fi
    
    # 方法2: 检查常见的输出目录
    local possible_dirs=(
        "$PROJECT_ROOT/src/StockAnalyse.Api/wwwroot"  # Vite 配置的默认输出
        "$PROJECT_ROOT/frontend/dist"                  # 标准 Vite 输出
        "$PROJECT_ROOT/frontend/build"                 # 某些构建工具的输出
        "$PROJECT_ROOT/frontend/wwwroot"               # 备用输出
        "$dist_source"                                 # 从配置读取的目录
    )
    
    # 如果还没有找到，尝试查找包含 index.html 的目录
    if [[ -z "$dist_source" ]] || [[ ! -d "$dist_source" ]]; then
        log_info "检查常见的构建输出目录..."
        for dir in "${possible_dirs[@]}"; do
            if [[ -n "$dir" ]] && [[ -d "$dir" ]] && [[ -f "$dir/index.html" ]]; then
                dist_source="$dir"
                log_info "找到构建输出目录: $dist_source"
                break
            fi
        done
    fi
    
    # 方法3: 如果还是没找到，搜索包含 index.html 的目录
    if [[ -z "$dist_source" ]] || [[ ! -d "$dist_source" ]] || [[ ! -f "$dist_source/index.html" ]]; then
        log_info "搜索包含 index.html 的目录..."
        local found_dir
        found_dir=$(run_as_service_user "find '$PROJECT_ROOT' -type f -name 'index.html' -path '*/wwwroot/*' -o -path '*/dist/*' -o -path '*/build/*' 2>/dev/null | head -1 | xargs dirname 2>/dev/null || echo ''" || echo "")
        
        if [[ -n "$found_dir" ]] && [[ -d "$found_dir" ]]; then
            dist_source="$found_dir"
            log_info "通过搜索找到构建输出目录: $dist_source"
        fi
    fi
    
    # 最终检查
    if [[ -z "$dist_source" ]] || [[ ! -d "$dist_source" ]]; then
        log_info "⚠️  无法确定构建输出目录"
        log_info "已检查的目录:"
        for dir in "${possible_dirs[@]}"; do
            if [[ -n "$dir" ]]; then
                log_info "  - $dir ($([ -d "$dir" ] && echo "存在" || echo "不存在"))"
            fi
        done
        log_info "前端目录结构:"
        run_as_service_user "cd '$PROJECT_ROOT/frontend' && find . -maxdepth 3 -type d -name 'dist' -o -name 'build' -o -name 'wwwroot' 2>/dev/null | head -10" 2>&1 || true
        log_info "提示: 检查前端构建配置中的输出目录设置"
        return
    fi
    
    # 检查 index.html 是否存在
    if [[ ! -f "$dist_source/index.html" ]]; then
        log_info "⚠️  构建输出目录存在，但未找到 index.html: $dist_source"
        log_info "输出目录内容:"
        run_as_service_user "ls -lah '$dist_source'" 2>&1 | head -20 || true
        
        # 检查是否有其他 HTML 文件
        local html_files
        html_files=$(run_as_service_user "find '$dist_source' -name '*.html' -type f 2>/dev/null | head -5" || echo "")
        if [[ -n "$html_files" ]]; then
            log_info "找到其他 HTML 文件:"
            echo "$html_files" | sed 's/^/   /'
        fi
        
        log_info "可能的原因:"
        log_info "  1. 构建过程中出现错误但未正确报告"
        log_info "  2. 前端框架使用了不同的入口文件名"
        log_info "提示: 检查构建输出日志"
        return
    fi
    
    log_info "✓ 找到构建输出目录: $dist_source"
    log_info "✓ 找到 index.html: $dist_source/index.html"
    
    local dist_target="$FRONTEND_DIST_DIR"

    log_info "前端构建成功，输出目录: $dist_source"
    log_info "目标目录: $dist_target"
    
    # 检查源目录和目标目录是否相同（规范化路径后比较）
    local source_normalized target_normalized
    source_normalized=$(run_as_service_user "cd '$PROJECT_ROOT' && realpath '$dist_source' 2>/dev/null || echo '$dist_source'" || echo "$dist_source")
    target_normalized=$(run_as_service_user "cd '$PROJECT_ROOT' && realpath '$dist_target' 2>/dev/null || echo '$dist_target'" || echo "$dist_target")
    
    # 如果路径相同，跳过复制
    if [[ "$source_normalized" == "$target_normalized" ]]; then
        log_ok "构建输出目录与目标目录相同，无需复制"
        log_info "文件已在正确位置: $dist_source"
        log_info "验证: index.html 存在，文件大小: $(du -h '$dist_source/index.html' | cut -f1)"
    else
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
        log_info "从: $dist_source"
        log_info "到: $dist_target"
        
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
    fi
    
    # 设置正确的权限（如果源和目标相同，使用源目录）
    local perm_dir="$dist_target"
    if [[ "$source_normalized" == "$target_normalized" ]]; then
        perm_dir="$dist_source"
    fi
    
    log_info "设置文件权限..."
    sudo find "$perm_dir" -type d -exec chmod 755 {} \;
    sudo find "$perm_dir" -type f -exec chmod 644 {} \;
    sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$perm_dir"
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
    
    # 首先尝试使用 dotnet ef database update（推荐方式）
    if [[ -d "$PROJECT_ROOT/src/StockAnalyse.Api" ]]; then
        log_info "执行数据库迁移 (使用 dotnet ef)..."
        
        # 检查 dotnet ef 工具是否已安装
        if ! run_as_service_user "cd '$PROJECT_ROOT/src/StockAnalyse.Api' && dotnet ef --version" >/dev/null 2>&1; then
            log_info "安装 dotnet ef 工具..."
            run_as_service_user "dotnet tool install --global dotnet-ef" 2>&1 || {
                log_info "⚠️  dotnet ef 工具安装失败，尝试使用备用方法"
            }
        fi
        
        # 确保端口未被占用（数据库迁移可能会启动临时服务）
        free_port "$BACKEND_PORT"
        
        # 使用 set +e 允许迁移失败时继续
        set +e
        log_info "执行: dotnet ef database update"
        local migrate_output
        migrate_output=$(run_as_service_user "cd '$PROJECT_ROOT/src/StockAnalyse.Api' && dotnet ef database update" 2>&1)
        local migrate_status=$?
        set -e
        
        if [[ $migrate_status -eq 0 ]]; then
            log_ok "数据库迁移完成 (dotnet ef)"
            # 显示迁移输出的最后几行（通常包含成功信息）
            echo "$migrate_output" | tail -5 | sed 's/^/   /' || true
        else
            log_info "⚠️  dotnet ef 迁移失败，尝试备用方法..."
            echo "$migrate_output" | tail -10 | sed 's/^/   /' || true
            
            # 备用方法：使用 DLL 的迁移参数（如果支持）
            if [[ -f "$BACKEND_PUBLISH_DIR/StockAnalyse.Api.dll" ]]; then
                log_info "尝试使用备用迁移方法..."
                free_port "$BACKEND_PORT"
                set +e
                run_as_service_user "cd '$BACKEND_PUBLISH_DIR' && timeout 60 dotnet StockAnalyse.Api.dll --migrate-database" 2>&1 | head -20 || true
                local backup_migrate_status=$?
                set -e
                
                if [[ $backup_migrate_status -eq 0 ]]; then
                    log_ok "数据库迁移完成 (备用方法)"
                else
                    log_info "⚠️  数据库迁移可能失败，但继续部署流程"
                    log_info "提示: 可以稍后手动运行迁移命令: cd $PROJECT_ROOT/src/StockAnalyse.Api && dotnet ef database update"
                fi
            else
                log_info "⚠️  未找到后端可执行文件，跳过数据库迁移"
                log_info "提示: 可以稍后手动运行迁移命令: cd $PROJECT_ROOT/src/StockAnalyse.Api && dotnet ef database update"
            fi
        fi
        
        # 迁移后再次清理端口，确保没有残留进程
        free_port "$BACKEND_PORT"
    elif [[ -f "$BACKEND_PUBLISH_DIR/StockAnalyse.Api.dll" ]]; then
        # 如果项目目录不存在，但发布目录存在，尝试使用 DLL 方式
        log_info "项目目录不存在，使用发布目录执行迁移..."
        free_port "$BACKEND_PORT"
        
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
        
        free_port "$BACKEND_PORT"
    else
        log_info "未找到后端项目目录或可执行文件，跳过数据库迁移"
        log_info "提示: 可以稍后手动运行迁移命令: cd $PROJECT_ROOT/src/StockAnalyse.Api && dotnet ef database update"
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

