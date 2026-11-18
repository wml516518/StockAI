#!/usr/bin/env bash
set -Eeuo pipefail

die() {
    echo "错误: $*" >&2
    exit 1
}

log() {
    printf '\n==> %s\n' "$*"
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

set_env_value() {
    local key="$1"
    local value="$2"
    local file="$3"
    local temporary
    temporary="$(mktemp "${file}.tmp.XXXXXX")"
    awk -v key="$key" -v value="$value" '
        index($0, key "=") == 1 {
            if (!written) {
                print key "=" value
                written = 1
            }
            next
        }
        { print }
        END {
            if (!written) {
                print key "=" value
            }
        }
    ' "$file" > "$temporary"
    chmod 600 "$temporary"
    mv "$temporary" "$file"
}

usage() {
    cat <<'EOF'
用法:
  sudo APP_DIR=/opt/stockai STOCKAI_DOMAIN=stock.example.com \
    LETSENCRYPT_EMAIL=you@example.com \
    bash deploy-stockai.sh /root/stockai-release.tar.gz

可选环境变量:
  APP_DIR                   安装目录，默认 /opt/stockai
  STOCKAI_DOMAIN            域名；未设置时只启动本机服务
  LETSENCRYPT_EMAIL         配置 Let's Encrypt HTTPS 时使用
  STOCKAI_AUTH_USERNAME     首次安装的登录用户名，默认 stockai
  STOCKAI_AUTH_PASSWORD     首次安装密码；未设置时自动生成
  STOCKAI_RELEASE_ID        CI/CD 发布编号，例如 v1.2.0
  STOCKAI_BASE_PATH         前端子路径，默认 /；接入个人主页时用 /apps/stockai/
  NUGET_SOURCE              NuGet 源，默认官方源
  APT_MIRROR                Ubuntu 镜像主机，例如 mirrors.aliyun.com
EOF
}

[[ "${EUID}" -eq 0 ]] || die "请使用 root 或 sudo 运行"

ARCHIVE="${1:-}"
[[ -n "$ARCHIVE" ]] || {
    usage
    exit 1
}
[[ -f "$ARCHIVE" ]] || die "发布包不存在: $ARCHIVE"

APP_DIR="${APP_DIR:-/opt/stockai}"
DOMAIN="${STOCKAI_DOMAIN:-}"
EMAIL="${LETSENCRYPT_EMAIL:-}"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
APT_MIRROR="${APT_MIRROR:-}"
RELEASE_ID="${STOCKAI_RELEASE_ID:-manual-$(date +%Y%m%d-%H%M%S)}"
[[ "$RELEASE_ID" =~ ^[A-Za-z0-9._-]{1,64}$ ]] \
    || die "STOCKAI_RELEASE_ID 格式不正确"

BASE_PATH="${STOCKAI_BASE_PATH:-/}"
[[ "$BASE_PATH" == "/" || "$BASE_PATH" =~ ^/[A-Za-z0-9._/-]+/$ ]] \
    || die "STOCKAI_BASE_PATH 必须以 / 开头和结尾，例如 /apps/stockai/"
[[ "$BASE_PATH" != *".."* ]] || die "STOCKAI_BASE_PATH 不能包含 .."

log "检查服务器"
case "$(uname -m)" in
    x86_64)
        export DOCKER_DEFAULT_PLATFORM=linux/amd64
        ;;
    aarch64|arm64)
        export DOCKER_DEFAULT_PLATFORM=linux/arm64
        ;;
    *)
        die "不支持的 CPU 架构: $(uname -m)"
        ;;
esac

command -v docker >/dev/null 2>&1 \
    || die "未安装 Docker。请先运行 deploy/server/install-docker.sh"
docker compose version >/dev/null 2>&1 \
    || die "未安装 Docker Compose v2，请先运行 deploy/server/install-docker.sh"
command -v curl >/dev/null 2>&1 || die "缺少 curl"
command -v tar >/dev/null 2>&1 || die "缺少 tar"

AVAILABLE_KB="$(df -Pk /var/lib | awk 'NR == 2 {print $4}')"
((AVAILABLE_KB >= 4 * 1024 * 1024)) \
    || die "根分区可用空间不足 4 GB，当前约 $((AVAILABLE_KB / 1024)) MB"

log "校验发布包"
if tar -tzf "$ARCHIVE" | grep -Eq '(^|/)\.env$|(^|/)config\.ini$'; then
    die "发布包包含 .env 或 config.ini，拒绝部署"
fi

TEMP_DIR="$(mktemp -d)"
cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT
tar -xzf "$ARCHIVE" -C "$TEMP_DIR"

SOURCE_DIR="$TEMP_DIR"
if [[ ! -f "$SOURCE_DIR/docker-compose.yml" ]]; then
    COMPOSE_PATH="$(find "$TEMP_DIR" -maxdepth 2 -name docker-compose.yml -print -quit)"
    [[ -n "$COMPOSE_PATH" ]] || die "发布包中找不到 docker-compose.yml"
    SOURCE_DIR="$(dirname "$COMPOSE_PATH")"
fi

for required in \
    docker-compose.yml \
    frontend/Dockerfile \
    python-data-service/Dockerfile \
    python-data-service/config.example.ini \
    src/StockAnalyse.Api/Dockerfile; do
    [[ -e "$SOURCE_DIR/$required" ]] || die "发布包缺少: $required"
done

log "准备安装目录 $APP_DIR"
if [[ -e "$APP_DIR" && ! -f "$APP_DIR/.stockai-managed" ]]; then
    if [[ -n "$(find "$APP_DIR" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
        die "$APP_DIR 非空且不是本脚本管理的目录；请换 APP_DIR 或手动确认"
    fi
fi
mkdir -p "$APP_DIR"
tar -C "$SOURCE_DIR" -cf - . | tar -C "$APP_DIR" -xf -
chown -R root:root "$APP_DIR"
chmod +x \
    "$APP_DIR/deploy/server/deploy-stockai.sh" \
    "$APP_DIR/deploy/server/configure-nginx.sh" \
    "$APP_DIR/deploy/server/verify-stockai.sh" \
    "$APP_DIR/deploy/server/ci-deploy-wrapper.sh" \
    "$APP_DIR/deploy/server/setup-cicd-user.sh" \
    "$APP_DIR/deploy/server/setup-cicd-existing-user.sh"
printf 'managed-by=stockai-deploy\nrelease-id=%s\ninstalled-at=%s\n' \
    "$RELEASE_ID" "$(date -Iseconds)" > "$APP_DIR/.stockai-managed"

log "准备访问凭据与 Python 配置"
ENV_FILE="$APP_DIR/.env"
[[ -f "$ENV_FILE" ]] || cp "$APP_DIR/.env.example" "$ENV_FILE"
chmod 600 "$ENV_FILE"

EXISTING_USERNAME="$(read_env_value APP_AUTH_USERNAME "$ENV_FILE")"
if [[ -n "${STOCKAI_AUTH_USERNAME:-}" ]]; then
    AUTH_USERNAME="$STOCKAI_AUTH_USERNAME"
elif [[ -n "$EXISTING_USERNAME" ]]; then
    AUTH_USERNAME="$EXISTING_USERNAME"
else
    AUTH_USERNAME="stockai"
fi
[[ "$AUTH_USERNAME" =~ ^[A-Za-z0-9._-]{1,64}$ ]] \
    || die "登录用户名只能包含字母、数字、点、下划线和连字符"

EXISTING_PASSWORD="$(read_env_value APP_AUTH_PASSWORD "$ENV_FILE")"
if [[ -n "${STOCKAI_AUTH_PASSWORD:-}" ]]; then
    AUTH_PASSWORD="$STOCKAI_AUTH_PASSWORD"
elif [[ -n "$EXISTING_PASSWORD" ]]; then
    AUTH_PASSWORD="$EXISTING_PASSWORD"
elif command -v openssl >/dev/null 2>&1; then
    AUTH_PASSWORD="$(openssl rand -hex 24)"
else
    AUTH_PASSWORD="$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')"
fi
if [[ -n "${STOCKAI_AUTH_PASSWORD:-}" ]]; then
    [[ "$AUTH_PASSWORD" =~ ^[A-Za-z0-9._~!@%^*+=-]{20,128}$ ]] \
        || die "新密码需为 20-128 位，且不能包含空格、引号、反斜杠或 #"
fi

PORT="$(read_env_value HTTP_PORT "$ENV_FILE")"
PORT="${PORT:-18081}"
if ! [[ "$PORT" =~ ^[0-9]+$ ]] || ((PORT < 1 || PORT > 65535)); then
    PORT=18081
fi

set_env_value APP_AUTH_USERNAME "$AUTH_USERNAME" "$ENV_FILE"
set_env_value APP_AUTH_PASSWORD "$AUTH_PASSWORD" "$ENV_FILE"
set_env_value HTTP_BIND_ADDRESS "127.0.0.1" "$ENV_FILE"
set_env_value HTTP_PORT "$PORT" "$ENV_FILE"
set_env_value VITE_BASE_PATH "$BASE_PATH" "$ENV_FILE"

CONFIG_FILE="$APP_DIR/python-data-service/config.ini"
if [[ ! -f "$CONFIG_FILE" ]]; then
    cp "$APP_DIR/python-data-service/config.example.ini" "$CONFIG_FILE"
fi
chown 1000:1000 "$CONFIG_FILE"
chmod 600 "$CONFIG_FILE"
mkdir -p "$APP_DIR/backups"
chmod 700 "$APP_DIR/backups"

PROJECT_NAME="$(basename "$APP_DIR" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9_-')"
PROJECT_NAME="${PROJECT_NAME:-stockai}"
cd "$APP_DIR"
docker compose config --quiet
COMPOSE_CONFIG="$(docker compose config)"
DATA_VOLUME="$(awk '
    /^  stock-data:/ { found = 1; next }
    found && /^    name:/ { print $2; exit }
' <<<"$COMPOSE_CONFIG")"
DATA_VOLUME="${DATA_VOLUME:-${PROJECT_NAME}_stock-data}"

if docker volume inspect "$DATA_VOLUME" >/dev/null 2>&1; then
    BACKUP_IMAGE="${PROJECT_NAME}-python-service:latest"
    if ! docker image inspect "$BACKUP_IMAGE" >/dev/null 2>&1; then
        BACKUP_IMAGE="python:3.12-slim"
    fi
    if docker run --rm -v "$DATA_VOLUME:/source:ro" \
        "$BACKUP_IMAGE" test -f /source/stockanalyse.db >/dev/null 2>&1; then
        log "备份现有 SQLite 数据库"
        BACKUP_FILE="stockanalyse-$(date +%Y%m%d-%H%M%S).db"
        docker run --rm --user 0:0 \
            -v "$DATA_VOLUME:/source:ro" \
            -v "$APP_DIR/backups:/backup" \
            "$BACKUP_IMAGE" python -c \
            "import sqlite3; source=sqlite3.connect('/source/stockanalyse.db'); target=sqlite3.connect('/backup/${BACKUP_FILE}'); source.backup(target); target.close(); source.close()"
        chmod 600 "$APP_DIR/backups/$BACKUP_FILE"
        echo "数据库备份: $APP_DIR/backups/$BACKUP_FILE"
    else
        echo "数据卷存在但尚无数据库文件，跳过备份。"
    fi
else
    echo "首次安装，尚无数据库卷可备份。"
fi

log "构建容器镜像"
BUILD_ARGS=(--build-arg "NUGET_SOURCE=$NUGET_SOURCE")
if [[ -n "$APT_MIRROR" ]]; then
    BUILD_ARGS+=(--build-arg "APT_MIRROR=$APT_MIRROR")
fi
docker compose build "${BUILD_ARGS[@]}" backend
docker compose build frontend python-service

log "启动服务并等待健康检查"
if ! docker compose up -d --no-build --wait --wait-timeout 300; then
    docker compose ps
    docker compose logs --tail=100 backend python-service frontend
    die "服务未能全部健康启动，旧数据卷未被删除"
fi

curl -fsS --max-time 10 "http://127.0.0.1:${PORT}/health" >/dev/null \
    || die "本机健康检查失败"

if [[ -n "$DOMAIN" ]]; then
    log "配置 Nginx 与 HTTPS"
    export APP_DIR
    STOCKAI_DOMAIN="$DOMAIN" \
        LETSENCRYPT_EMAIL="$EMAIL" \
        bash "$APP_DIR/deploy/server/configure-nginx.sh"
else
    echo
    echo "应用只监听 127.0.0.1:${PORT}。配置公网域名后运行:"
    echo "sudo APP_DIR=$APP_DIR STOCKAI_DOMAIN=stock.example.com LETSENCRYPT_EMAIL=you@example.com bash $APP_DIR/deploy/server/configure-nginx.sh"
fi

echo
echo "StockAI 部署完成，发布编号: $RELEASE_ID"
echo "登录用户名: $AUTH_USERNAME"
echo "密码请在本机查看: sudo grep '^APP_AUTH_PASSWORD=' $ENV_FILE"
if [[ -n "$DOMAIN" ]]; then
    echo "上线验收: sudo APP_DIR=$APP_DIR BASE_URL=https://${DOMAIN} bash $APP_DIR/deploy/server/verify-stockai.sh"
else
    echo "上线验收: sudo APP_DIR=$APP_DIR BASE_URL=http://127.0.0.1:${PORT} bash $APP_DIR/deploy/server/verify-stockai.sh"
fi
