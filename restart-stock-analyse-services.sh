#!/bin/bash

# ====================================================
# 一键重启服务脚本 (Backend + Python + Nginx)
# 与 deploy-stock-analyse-simple.sh 配套
# ====================================================

set -Eeuo pipefail

log_step() {
    echo ""
    echo "==============================="
    echo "🔄  $1"
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

restart_service() {
    local svc="$1"
    if service_exists "$svc"; then
        if systemctl is-enabled --quiet "$svc"; then
            sudo systemctl restart "$svc" || sudo systemctl start "$svc" || true
            log_ok "已重启: $svc"
        else
            sudo systemctl start "$svc" || true
            log_ok "已启动(原未启用): $svc"
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
    log_step "重启后端与数据服务"
    restart_service "stock-backend"
    restart_service "stock-python"

    log_step "重启 Nginx"
    if command -v nginx >/dev/null 2>&1; then
        sudo systemctl restart nginx || sudo systemctl start nginx || true
        log_ok "Nginx 重启完成"
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
    echo "✅ 完成：服务已重启。"
}

main "$@"


