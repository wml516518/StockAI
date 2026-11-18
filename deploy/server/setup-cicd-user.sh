#!/usr/bin/env bash
set -Eeuo pipefail

die() {
    echo "错误: $*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
用法:
  sudo STOCKAI_DOMAIN=stock.example.com \
    LETSENCRYPT_EMAIL=you@example.com \
    bash setup-cicd-user.sh /root/stockai-ci.pub

可选环境变量:
  DEPLOY_USER           SSH 部署用户，默认 stockai-ci
  APP_DIR               StockAI 安装目录，默认 /opt/stockai
  STOCKAI_DOMAIN        生产域名
  LETSENCRYPT_EMAIL     HTTPS 通知邮箱
  NUGET_SOURCE          NuGet 源
  APT_MIRROR            Ubuntu 镜像主机
EOF
}

WRAPPER_PATH="/usr/local/sbin/stockai-ci-deploy"
EMAIL_PATTERN='^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'
NUGET_PATTERN='^https://[A-Za-z0-9._~:/?#@!+=$&%-]+$'

authorized_keys_line() {
    local sudo_path="$1"
    local public_key="$2"
    # 保留字面量，由 sshd 启动的登录 Shell 展开后再作为单个参数传入。
    # shellcheck disable=SC2016
    printf 'restrict,command="%s -n %s \\"%s\\"" %s\n' \
        "$sudo_path" "$WRAPPER_PATH" '$SSH_ORIGINAL_COMMAND' "$public_key"
}

# 供 tests/test_server_deployment.py 以只读方式加载函数，不执行任何安装步骤。
if [[ "${STOCKAI_SETUP_LIB_ONLY:-0}" == "1" ]]; then
    return 0
fi

[[ "${EUID}" -eq 0 ]] || die "请使用 root 或 sudo 运行"
[[ $# -eq 1 ]] || {
    usage
    exit 1
}

PUBKEY_FILE="$1"
[[ -f "$PUBKEY_FILE" ]] || die "公钥文件不存在: $PUBKEY_FILE"

DEPLOY_USER="${DEPLOY_USER:-stockai-ci}"
APP_DIR="${APP_DIR:-/opt/stockai}"
STOCKAI_DOMAIN="${STOCKAI_DOMAIN:-}"
EMAIL="${LETSENCRYPT_EMAIL:-}"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
APT_MIRROR="${APT_MIRROR:-}"

[[ "$DEPLOY_USER" =~ ^[a-z_][a-z0-9_-]{0,31}$ ]] \
    || die "DEPLOY_USER 不是有效的 Linux 用户名"
[[ "$APP_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] || die "APP_DIR 无效"
[[ "$APP_DIR" != *".."* ]] || die "APP_DIR 不能包含 .."
if [[ -n "$EMAIL" && ! "$EMAIL" =~ $EMAIL_PATTERN ]]; then
    die "LETSENCRYPT_EMAIL 格式不正确"
fi
[[ "$NUGET_SOURCE" =~ $NUGET_PATTERN ]] \
    || die "NUGET_SOURCE 必须是 https 地址"
if [[ -n "$APT_MIRROR" && ! "$APT_MIRROR" =~ ^[A-Za-z0-9.-]+$ ]]; then
    die "APT_MIRROR 格式不正确"
fi
if [[ -n "$STOCKAI_DOMAIN" && ! "$STOCKAI_DOMAIN" =~ ^([A-Za-z0-9-]+\.)+[A-Za-z]{2,}$ ]]; then
    die "STOCKAI_DOMAIN 格式不正确"
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WRAPPER_SOURCE="$SCRIPT_DIR/ci-deploy-wrapper.sh"
[[ -f "$WRAPPER_SOURCE" ]] || die "缺少 $WRAPPER_SOURCE"

if ! command -v docker >/dev/null 2>&1 \
    || ! docker compose version >/dev/null 2>&1; then
    [[ -f "$SCRIPT_DIR/install-docker.sh" ]] \
        || die "未安装 Docker/Compose v2；请同时上传 install-docker.sh"
    echo "未检测到 Docker/Compose v2，开始安装..."
    bash "$SCRIPT_DIR/install-docker.sh"
fi

PUBLIC_KEY="$(tr -d '\r' < "$PUBKEY_FILE" | awk 'NF {print; exit}')"
[[ "$PUBLIC_KEY" =~ ^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp256)[[:space:]] ]] \
    || die "公钥文件格式不正确"

command -v sudo >/dev/null 2>&1 || die "缺少 sudo"
SUDO_PATH="$(command -v sudo)"
BASH_PATH="$(command -v bash)"

if ! id "$DEPLOY_USER" >/dev/null 2>&1; then
    useradd --create-home --shell "$BASH_PATH" "$DEPLOY_USER"
fi
passwd -l "$DEPLOY_USER" >/dev/null 2>&1 || true

install -o root -g root -m 0755 \
    "$WRAPPER_SOURCE" "$WRAPPER_PATH"

install -d -o root -g root -m 0755 /etc/stockai
CONFIG_FILE="/etc/stockai/deploy.env"
if [[ ! -f "$CONFIG_FILE" ]]; then
    (
        umask 077
        {
            printf 'APP_DIR=%q\n' "$APP_DIR"
            printf 'STOCKAI_DOMAIN=%q\n' "$STOCKAI_DOMAIN"
            printf 'LETSENCRYPT_EMAIL=%q\n' "$EMAIL"
            printf 'NUGET_SOURCE=%q\n' "$NUGET_SOURCE"
            printf 'APT_MIRROR=%q\n' "$APT_MIRROR"
        } > "$CONFIG_FILE"
    )
    chmod 0600 "$CONFIG_FILE"
else
    CURRENT_MODE="$(stat -c '%u:%a' "$CONFIG_FILE")"
    [[ "$CURRENT_MODE" == "0:600" ]] \
        || die "$CONFIG_FILE 权限必须是 root:root 600"
fi

USER_HOME="$(getent passwd "$DEPLOY_USER" | cut -d: -f6)"
[[ -n "$USER_HOME" ]] || die "无法读取 $DEPLOY_USER 的主目录"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 0700 "$USER_HOME/.ssh"
AUTHORIZED_KEYS="$USER_HOME/.ssh/authorized_keys"
authorized_keys_line "$SUDO_PATH" "$PUBLIC_KEY" > "$AUTHORIZED_KEYS"
chown "$DEPLOY_USER:$DEPLOY_USER" "$AUTHORIZED_KEYS"
chmod 0600 "$AUTHORIZED_KEYS"

SUDOERS_FILE="/etc/sudoers.d/stockai-ci-deploy"
cat > "$SUDOERS_FILE" <<EOF
$DEPLOY_USER ALL=(root) NOPASSWD: /usr/local/sbin/stockai-ci-deploy
EOF
chmod 0440 "$SUDOERS_FILE"
if ! visudo -cf "$SUDOERS_FILE" >/dev/null; then
    rm -f "$SUDOERS_FILE"
    die "sudoers 校验失败，已撤销该文件"
fi

echo "CI/CD 部署用户已配置: $DEPLOY_USER"
echo "公钥指纹: $(ssh-keygen -lf "$PUBKEY_FILE")"
echo "部署配置: $CONFIG_FILE"
echo "部署日志: /var/log/stockai-ci-deploy.log"
echo
echo "GitHub Secrets 需要设置:"
echo "  DEPLOY_HOST    = 8.148.30.195"
echo "  DEPLOY_PORT    = 22"
echo "  DEPLOY_USER    = $DEPLOY_USER"
echo "  DEPLOY_SSH_KEY = 本公钥对应的私钥全文"
echo "  DEPLOY_KNOWN_HOSTS = ssh-keyscan -p 22 8.148.30.195 的输出"
