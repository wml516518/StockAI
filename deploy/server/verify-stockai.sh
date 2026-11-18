#!/usr/bin/env bash
set -Eeuo pipefail

fail() {
    echo "失败: $*" >&2
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

APP_DIR="${APP_DIR:-/opt/stockai}"
BASE_URL="${BASE_URL:-${1:-}}"
if [[ -z "$BASE_URL" && -n "${STOCKAI_DOMAIN:-}" ]]; then
    BASE_URL="https://${STOCKAI_DOMAIN}"
fi
[[ -n "$BASE_URL" ]] || fail "请设置 BASE_URL=https://你的域名 或 STOCKAI_DOMAIN"
BASE_URL="${BASE_URL%/}"

ENV_FILE="$APP_DIR/.env"
[[ -f "$ENV_FILE" ]] || fail "找不到 $ENV_FILE"
AUTH_USERNAME="$(read_env_value APP_AUTH_USERNAME "$ENV_FILE")"
AUTH_PASSWORD="$(read_env_value APP_AUTH_PASSWORD "$ENV_FILE")"
[[ -n "$AUTH_USERNAME" && -n "$AUTH_PASSWORD" ]] || fail "登录凭据不完整"

TEMP_DIR="$(mktemp -d)"
cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

AUTH_PAIR="${AUTH_USERNAME}:${AUTH_PASSWORD}"
curl_auth() {
    local escaped="$AUTH_PAIR"
    escaped="${escaped//\\/\\\\}"
    escaped="${escaped//\"/\\\"}"
    printf 'user = "%s"\n' "$escaped" | curl --config - "$@"
}

echo "检查入口: $BASE_URL"

HEALTH="$(curl -fsS --max-time 10 "$BASE_URL/health")" \
    || fail "健康检查不可访问"
grep -q '"status":"healthy"' <<<"$HEALTH" \
    || fail "健康检查未报告 healthy: $HEALTH"
echo "✓ 健康检查"

STATUS="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "$BASE_URL/")"
[[ "$STATUS" == "401" ]] || fail "未登录访问首页应返回 401，实际 $STATUS"
echo "✓ 首页要求认证"

curl_auth -fsS --max-time 15 "$BASE_URL/" > "$TEMP_DIR/index.html" \
    || fail "认证后首页不可访问"
grep -q 'id="app"' "$TEMP_DIR/index.html" \
    || fail "首页内容不符合预期"
echo "✓ 认证后首页"

curl_auth -fsS --max-time 15 "$BASE_URL/api/aiprompts" >/dev/null \
    || fail "认证后业务 API 不可访问"
echo "✓ 业务 API"

curl_auth -sS --max-time 10 -D "$TEMP_DIR/headers" \
    -o /dev/null "$BASE_URL/"
grep -qi '^x-content-type-options: *nosniff' "$TEMP_DIR/headers" \
    || fail "缺少 X-Content-Type-Options"
grep -qi '^x-frame-options: *DENY' "$TEMP_DIR/headers" \
    || fail "缺少 X-Frame-Options"
grep -qi '^referrer-policy: *same-origin' "$TEMP_DIR/headers" \
    || fail "缺少 Referrer-Policy"
echo "✓ 安全响应头"

curl_auth -sS -N --max-time 5 \
    -D "$TEMP_DIR/sse-headers" \
    -o "$TEMP_DIR/sse-body" \
    "$BASE_URL/api/watchlist/trading-plan/events" 2>/dev/null || true
grep -qi '^content-type: *text/event-stream' "$TEMP_DIR/sse-headers" \
    || fail "SSE Content-Type 不正确"
if grep -qi '^content-encoding:' "$TEMP_DIR/sse-headers"; then
    fail "SSE 不应被代理压缩"
fi
grep -q '"type":"connected"' "$TEMP_DIR/sse-body" \
    || fail "SSE 首包中没有 connected 事件"
echo "✓ SSE 未被缓冲或压缩"

if [[ "$BASE_URL" == https://* ]]; then
    grep -qi '^strict-transport-security:' "$TEMP_DIR/headers" \
        || fail "HTTPS 响应缺少 HSTS"
    echo "✓ HTTPS 与 HSTS"
fi

if [[ -f "$APP_DIR/docker-compose.yml" ]] && command -v docker >/dev/null 2>&1; then
    RUNNING="$(cd "$APP_DIR" && docker compose ps --status running --services)"
    for service in backend frontend python-service; do
        grep -qx "$service" <<<"$RUNNING" \
            || fail "容器未运行: $service"
    done
    echo "✓ 三个容器均在运行"
fi

echo
echo "上线验收通过: $BASE_URL"
