#!/bin/bash

# ====================================================
# 一键停止服务脚本 (Backend + Python + Nginx)
# 与 deploy-stock-analyse-simple.sh 配套
# ====================================================

set -Eeuo pipefail

log_step() {
    echo ""
    echo "==============================="
    echo "⏹  $1"
    echo "==============================="
}

log_ok() {
    echo "✓ $1"
}

log_info() {
    echo " - $1"
}

service_exists() {
    local svc="$1"
    systemctl list-unit-files | grep -q "^${svc}\>"
}

stop_service() {
    local svc="$1"
    if service_exists "$svc"; then
        if systemctl is-active --quiet "$svc"; then
            sudo systemctl stop "$svc" || true
            log_ok "已停止: $svc"
        else
            log_info "服务未运行: $svc"
        fi
    else
        log_info "未检测到服务定义，跳过: $svc"
    fi
}

print_status() {
    local svc="$1"
    if service_exists "$svc"; then
        systemctl status "$svc" --no-pager -l | head -n 8 || true
    else
        echo "$svc 未安装或未配置"
    fi
}

main() {
    log_step "停止后端与数据服务"
    stop_service "stock-backend"
    stop_service "stock-python"

    log_step "停止 Nginx"
    if command -v nginx >/dev/null 2>&1; then
        if systemctl is-active --quiet nginx; then
            sudo systemctl stop nginx || true
            log_ok "已停止: nginx"
        else
            log_info "Nginx 未运行"
        fi
    else
        log_info "未检测到 nginx 命令，跳过"
    fi

    log_step "服务状态概览"
    print_status "stock-backend"
    print_status "stock-python"
    if command -v nginx >/dev/null 2>&1; then
        systemctl status nginx --no-pager -l | head -n 8 || true
    else
        echo "nginx 未安装或未配置"
    fi

    echo ""
    echo "✅ 完成：服务已停止。"
    echo "提示：如需再次启动，可使用 systemctl start <service> 或重新运行部署脚本。"
}

main "$@"


