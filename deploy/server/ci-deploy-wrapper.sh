#!/usr/bin/env bash
set -Eeuo pipefail

die() {
    echo "CI 部署失败: $*" >&2
    exit 1
}

[[ "${EUID}" -eq 0 ]] || die "必须以 root 运行（受限 sudo 或 root 强制命令）"

CONFIG_FILE="/etc/stockai/deploy.env"
INCOMING_DIR="/var/lib/stockai-ci-deploy"
LOG_FILE="/var/log/stockai-ci-deploy.log"
LOCK_FILE="/run/stockai-ci-deploy.lock"
MAX_ARCHIVE_BYTES=$((200 * 1024 * 1024))

[[ -f "$CONFIG_FILE" ]] || die "缺少 $CONFIG_FILE"
[[ "$(stat -c '%u:%a' "$CONFIG_FILE")" == "0:600" ]] \
    || die "$CONFIG_FILE 必须是 root 所有且权限为 600"

install -d -m 0700 "$INCOMING_DIR"
touch "$LOG_FILE"
chmod 0600 "$LOG_FILE"
exec > >(tee -a "$LOG_FILE") 2>&1

(( $# <= 1 )) || die "仅允许一个部署参数"
ORIGINAL_COMMAND="${1:-${SSH_ORIGINAL_COMMAND:-}}"
if [[ ! "$ORIGINAL_COMMAND" =~ ^deploy\ ([0-9a-f]{64})\ ([A-Za-z0-9._-]{1,64})$ ]]; then
    die "仅允许: deploy <sha256> <release-id>"
fi

EXPECTED_SHA="${BASH_REMATCH[1]}"
RELEASE_ID="${BASH_REMATCH[2]}"

set -a
# shellcheck disable=SC1090
. "$CONFIG_FILE"
set +a

APP_DIR="${APP_DIR:-/opt/stockai}"
[[ "$APP_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] || die "APP_DIR 无效"
[[ "$APP_DIR" != *".."* ]] || die "APP_DIR 不能包含 .."

umask 077
WORK_DIR="$(mktemp -d "$INCOMING_DIR/run.XXXXXX")"
ARCHIVE="$WORK_DIR/release.tar.gz"
cleanup() {
    rm -rf "$WORK_DIR"
}
trap cleanup EXIT

echo "接收发布包: $RELEASE_ID"
head -c $((MAX_ARCHIVE_BYTES + 1)) > "$ARCHIVE"
ARCHIVE_SIZE="$(stat -c '%s' "$ARCHIVE")"
((ARCHIVE_SIZE <= MAX_ARCHIVE_BYTES)) \
    || die "发布包超过 200 MB"

ACTUAL_SHA="$(sha256sum "$ARCHIVE" | awk '{print $1}')"
[[ "$ACTUAL_SHA" == "$EXPECTED_SHA" ]] \
    || die "SHA256 不匹配，拒绝部署"

exec 9>"$LOCK_FILE"
flock -n 9 || die "已有部署任务正在执行"

DEPLOY_SCRIPT="$WORK_DIR/deploy/server/deploy-stockai.sh"
if ! tar -xzf "$ARCHIVE" -C "$WORK_DIR" ./deploy/server/deploy-stockai.sh 2>/dev/null; then
    tar -xzf "$ARCHIVE" -C "$WORK_DIR"
fi
if [[ ! -f "$DEPLOY_SCRIPT" ]]; then
    DEPLOY_SCRIPT="$(find "$WORK_DIR" -maxdepth 3 -type f \
        -path '*/deploy/server/deploy-stockai.sh' -print -quit)"
fi
[[ -n "$DEPLOY_SCRIPT" && -f "$DEPLOY_SCRIPT" ]] \
    || die "发布包缺少 deploy/server/deploy-stockai.sh"

export STOCKAI_RELEASE_ID="$RELEASE_ID"
bash "$DEPLOY_SCRIPT" "$ARCHIVE"
