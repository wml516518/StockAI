#!/usr/bin/env bash
set -Eeuo pipefail

die() {
    echo "错误: $*" >&2
    exit 1
}

read_env_value() {
    local key="$1"
    local file="$2"
    [[ -f "$file" ]] || return 0
    awk -v key="$key" '
        index($0, key "=") == 1 {
            value = substr($0, length(key) + 2)
            gsub(/^["'\'']|["'\'']$/, "", value)
            print value
            exit
        }
    ' "$file"
}

[[ "${EUID}" -eq 0 ]] || die "请使用 root 或 sudo 运行"

APP_DIR="${APP_DIR:-/opt/stockai}"
DOMAIN="${STOCKAI_DOMAIN:-}"
EMAIL="${LETSENCRYPT_EMAIL:-}"
ENV_FILE="$APP_DIR/.env"
NGINX_CONF="/etc/nginx/conf.d/stockai.conf"

[[ "$DOMAIN" =~ ^([A-Za-z0-9-]+\.)+[A-Za-z]{2,}$ ]] \
    || die "STOCKAI_DOMAIN 必须是完整域名，例如 stock.example.com"
[[ "$DOMAIN" != *".."* ]] || die "STOCKAI_DOMAIN 格式不正确"
[[ -f "$ENV_FILE" ]] || die "找不到 $ENV_FILE，请先运行 deploy-stockai.sh"

command -v nginx >/dev/null 2>&1 || die "未安装 Nginx"

PORT="$(read_env_value HTTP_PORT "$ENV_FILE")"
PORT="${PORT:-18081}"
if ! [[ "$PORT" =~ ^[0-9]+$ ]] || ((PORT < 1 || PORT > 65535)); then
    die "HTTP_PORT 无效: $PORT"
fi

curl -fsS --max-time 10 "http://127.0.0.1:${PORT}/health" >/dev/null \
    || die "应用健康检查失败，请先修复 StockAI 容器"

BACKUP=""
if [[ -f "$NGINX_CONF" ]]; then
    BACKUP="${NGINX_CONF}.bak.$(date +%Y%m%d%H%M%S)"
    cp -a "$NGINX_CONF" "$BACKUP"
fi

mkdir -p /etc/nginx/conf.d
cat > "$NGINX_CONF" <<EOF
# Managed by StockAI deploy/server/configure-nginx.sh
server {
    listen 80;
    listen [::]:80;
    server_name ${DOMAIN};

    client_max_body_size 100m;
    server_tokens off;
    add_header Strict-Transport-Security "max-age=31536000" always;

    location = /api/watchlist/trading-plan/events {
        proxy_pass http://127.0.0.1:${PORT};
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_set_header Cookie \$http_cookie;
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
        gzip off;
        add_header X-Accel-Buffering no;
    }

    location / {
        proxy_pass http://127.0.0.1:${PORT};
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_set_header Cookie \$http_cookie;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }
}
EOF

if ! nginx -t; then
    rm -f "$NGINX_CONF"
    if [[ -n "$BACKUP" ]]; then
        mv "$BACKUP" "$NGINX_CONF"
    fi
    die "Nginx 配置校验失败，已撤销本次修改"
fi

if command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet nginx; then
    systemctl reload nginx
else
    nginx -s reload
fi

curl -fsS --max-time 10 -H "Host: ${DOMAIN}" \
    "http://127.0.0.1/health" >/dev/null \
    || die "Nginx 已加载，但本机 HTTP 健康检查失败"

echo "HTTP 入口已配置: http://${DOMAIN}/"

if [[ -z "$EMAIL" ]]; then
    echo "未设置 LETSENCRYPT_EMAIL，已跳过 HTTPS。登录密码不应通过公网 HTTP 使用。"
    exit 0
fi

if ! command -v certbot >/dev/null 2>&1; then
    echo "正在安装 certbot..."
    if command -v apt-get >/dev/null 2>&1; then
        export DEBIAN_FRONTEND=noninteractive
        apt-get update
        apt-get install -y certbot python3-certbot-nginx
    elif command -v dnf >/dev/null 2>&1; then
        dnf install -y certbot python3-certbot-nginx
    elif command -v yum >/dev/null 2>&1; then
        yum install -y certbot python3-certbot-nginx
    else
        die "未找到可用的包管理器，请手动安装 certbot 后重试"
    fi
fi

command -v certbot >/dev/null 2>&1 \
    || die "certbot 安装失败，请手动安装后重试"

if ! getent ahostsv4 "$DOMAIN" >/dev/null 2>&1; then
    die "域名 ${DOMAIN} 尚未解析，请配置 DNS A/AAAA 记录后重试"
fi

certbot --nginx \
    --non-interactive \
    --agree-tos \
    --keep-until-expiring \
    --redirect \
    --email "$EMAIL" \
    -d "$DOMAIN"

nginx -t
if command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet nginx; then
    systemctl reload nginx
else
    nginx -s reload
fi

curl -fsS --max-time 15 "https://${DOMAIN}/health" >/dev/null \
    || die "HTTPS 已申请，但公网健康检查失败，请检查 DNS、80/443 端口和安全组"

echo "HTTPS 入口已启用: https://${DOMAIN}/"
