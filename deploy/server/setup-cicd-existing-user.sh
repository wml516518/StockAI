#!/usr/bin/env bash
set -Eeuo pipefail

die() {
    echo "错误: $*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
用法:
  sudo bash setup-cicd-existing-user.sh /root/stockai-ci.pub

复用服务器上已有的 SSH 账号，不创建新用户、不锁定密码。
默认使用 root；也可以指定已有普通用户：

  sudo DEPLOY_USER=ubuntu \
    bash setup-cicd-existing-user.sh /root/stockai-ci.pub

可选环境变量:
  DEPLOY_USER          已有 SSH 账号，默认 root
  APP_DIR              StockAI 安装目录，默认 /opt/stockai
  STOCKAI_DOMAIN       生产域名
  LETSENCRYPT_EMAIL    HTTPS 通知邮箱
  NUGET_SOURCE         NuGet 源
  APT_MIRROR           Ubuntu 镜像主机
EOF
}

WRAPPER_PATH="/usr/local/sbin/stockai-ci-deploy"
EMAIL_PATTERN='^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'
NUGET_PATTERN='^https://[A-Za-z0-9._~:/?#@!+=$&%-]+$'

authorized_keys_line() {
    local deploy_user="$1"
    local sudo_path="$2"
    local public_key="$3"
    local command

    if [[ "$deploy_user" == "root" ]]; then
        command="$WRAPPER_PATH"
    else
        command="$sudo_path -n $WRAPPER_PATH"
    fi

    # 保留字面量，由 sshd 启动的登录 Shell 展开后再作为单个参数传入。
    # shellcheck disable=SC2016
    printf 'restrict,command="%s \\"%s\\"" %s\n' \
        "$command" '$SSH_ORIGINAL_COMMAND' "$public_key"
}

# 供 tests/test_server_deployment.py 以只读方式加载函数，不执行安装步骤。
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

DEPLOY_USER="${DEPLOY_USER:-root}"
APP_DIR="${APP_DIR:-/opt/stockai}"
STOCKAI_DOMAIN="${STOCKAI_DOMAIN:-}"
EMAIL="${LETSENCRYPT_EMAIL:-}"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
APT_MIRROR="${APT_MIRROR:-}"

[[ "$DEPLOY_USER" =~ ^[a-z_][a-z0-9_-]{0,31}$ ]] \
    || die "DEPLOY_USER 不是有效的 Linux 用户名"
id "$DEPLOY_USER" >/dev/null 2>&1 \
    || die "账号 $DEPLOY_USER 不存在；本脚本不创建账号"
if [[ "$DEPLOY_USER" == "root" ]] && command -v sshd >/dev/null 2>&1; then
    ROOT_LOGIN="$(sshd -T 2>/dev/null \
        | awk '$1 == "permitrootlogin" {print $2; exit}' || true)"
    [[ "$ROOT_LOGIN" != "no" ]] \
        || die "服务器已禁止 root SSH 登录；请改用 DEPLOY_USER=已有账号"
fi
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

SUDO_PATH=""
if [[ "$DEPLOY_USER" != "root" ]]; then
    command -v sudo >/dev/null 2>&1 || die "缺少 sudo"
    SUDO_PATH="$(command -v sudo)"
fi

install -o root -g root -m 0755 "$WRAPPER_SOURCE" "$WRAPPER_PATH"

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

KEY_BLOB="$(awk '{print $2}' <<< "$PUBLIC_KEY")"
if [[ -f "$AUTHORIZED_KEYS" ]] \
    && grep -F -- "$KEY_BLOB" "$AUTHORIZED_KEYS" \
        | grep -v -- 'stockai-ci-deploy' >/dev/null; then
    die "该公钥已存在于 $AUTHORIZED_KEYS 且没有受限强制命令。请使用专用 CI 公钥，或先移除旧条目"
fi

if [[ -f "$AUTHORIZED_KEYS" ]]; then
    cp -p "$AUTHORIZED_KEYS" \
        "$AUTHORIZED_KEYS.bak.$(date +%Y%m%d-%H%M%S)"
fi

TEMPORARY_KEY_FILE="$(mktemp "$USER_HOME/.ssh/authorized_keys.XXXXXX")"
{
    if [[ -f "$AUTHORIZED_KEYS" ]]; then
        grep -v -- 'command=.*stockai-ci-deploy' "$AUTHORIZED_KEYS" || true
    fi
    authorized_keys_line "$DEPLOY_USER" "$SUDO_PATH" "$PUBLIC_KEY"
} > "$TEMPORARY_KEY_FILE"
chown "$DEPLOY_USER:$DEPLOY_USER" "$TEMPORARY_KEY_FILE"
chmod 0600 "$TEMPORARY_KEY_FILE"
mv "$TEMPORARY_KEY_FILE" "$AUTHORIZED_KEYS"

if [[ "$DEPLOY_USER" != "root" ]]; then
    SUDOERS_FILE="/etc/sudoers.d/stockai-ci-deploy"
    cat > "$SUDOERS_FILE" <<EOF
$DEPLOY_USER ALL=(root) NOPASSWD: $WRAPPER_PATH
EOF
    chmod 0440 "$SUDOERS_FILE"
    if ! visudo -cf "$SUDOERS_FILE" >/dev/null; then
        rm -f "$SUDOERS_FILE"
        die "sudoers 校验失败，已撤销该文件"
    fi
fi

echo "CI/CD 已绑定现有账号: $DEPLOY_USER"
echo "公钥指纹: $(ssh-keygen -lf "$PUBKEY_FILE")"
echo "部署配置: $CONFIG_FILE"
echo "部署日志: /var/log/stockai-ci-deploy.log"
echo "密钥备份目录: $USER_HOME/.ssh/"
echo
echo "GitHub production 环境需要设置:"
echo "  DEPLOY_HOST    = 8.148.30.195"
echo "  DEPLOY_PORT    = 22"
echo "  DEPLOY_USER    = $DEPLOY_USER"
echo "  DEPLOY_SSH_KEY = 本公钥对应的私钥全文"
echo "  DEPLOY_KNOWN_HOSTS = ssh-keyscan -p 22 8.148.30.195 的输出"
