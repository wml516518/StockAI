#!/usr/bin/env bash
# 诊断并修复无法从 Docker Hub 拉取基础镜像的问题。
set -uo pipefail

log() {
    printf '\n==> %s\n' "$*"
}

warn() {
    printf '警告: %s\n' "$*" >&2
}

[[ "${EUID}" -eq 0 ]] || {
    echo "请使用: sudo bash $0" >&2
    exit 1
}

REPORT="/tmp/stockai-docker-report-$(date +%Y%m%d-%H%M%S).txt"
exec > >(tee "$REPORT") 2>&1

log "0. 基本信息"
date -Is
if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    echo "${PRETTY_NAME:-unknown}"
fi
uname -m
docker version 2>&1 | sed -n '1,20p'
docker compose version 2>&1 | head -3
df -h /var/lib | tail -1

log "1. 当前 Docker registry 配置"
if [[ -f /etc/docker/daemon.json ]]; then
    cat /etc/docker/daemon.json
else
    echo "(没有 /etc/docker/daemon.json)"
fi
echo "registry mirrors: $(docker info --format '{{json .RegistryConfig.Mirrors}}' 2>/dev/null || echo unknown)"
echo "daemon proxy: $(systemctl show docker -p Environment --value 2>/dev/null || true)"

MIRRORS=(
    "https://docker.m.daocloud.io"
    "https://docker.1ms.run"
    "https://dockerproxy.net"
    "https://docker.1panel.live"
    "https://hub.rat.dev"
    "https://docker.rainbond.cc"
)

log "2. 检查镜像加速器可用性（实际拉取 hello-world）"
WORKING=""
for mirror in "${MIRRORS[@]}"; do
    code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 8 "$mirror/v2/" || true)"
    printf '%-32s /v2/ -> %s\n' "$mirror" "${code:-failed}"
    if [[ "$code" == "200" || "$code" == "401" ]]; then
        if timeout 90 docker pull "$mirror/library/hello-world:latest" >/dev/null 2>&1; then
            echo "  -> 可拉取镜像，选用该加速器"
            WORKING="$mirror"
            break
        fi
        echo "  -> 接口可达但镜像拉取失败"
    fi
done

restore_daemon() {
    if [[ -n "${DAEMON_BACKUP:-}" && -f "$DAEMON_BACKUP" ]]; then
        cp -p "$DAEMON_BACKUP" /etc/docker/daemon.json
    else
        rm -f /etc/docker/daemon.json
    fi
    systemctl restart docker >/dev/null 2>&1 || true
}

if [[ -n "$WORKING" ]]; then
    log "3. 写入 registry mirror 并重启 Docker"
    install -d -m 0755 /etc/docker
    if [[ -f /etc/docker/daemon.json ]]; then
        DAEMON_BACKUP="/etc/docker/daemon.json.stockai-backup.$(date +%Y%m%d-%H%M%S)"
        cp -p /etc/docker/daemon.json "$DAEMON_BACKUP"
        echo "已备份: $DAEMON_BACKUP"
    fi
    python3 - "$WORKING" <<'PY'
import json
import os
import sys

path = "/etc/docker/daemon.json"
mirror = sys.argv[1]
data = {}
if os.path.exists(path) and os.path.getsize(path) > 0:
    try:
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
    except Exception as exc:
        print(f"现有 daemon.json 无法解析（{exc}），用新配置替换，原文件已备份")
        data = {}
mirrors = data.get("registry-mirrors")
if not isinstance(mirrors, list):
    mirrors = []
mirrors = [item for item in mirrors if item != mirror]
mirrors.insert(0, mirror)
data["registry-mirrors"] = mirrors
with open(path, "w", encoding="utf-8") as handle:
    json.dump(data, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
print("写入 registry-mirrors:", mirrors)
PY
    if ! systemctl restart docker; then
        warn "Docker 重启失败，回滚 daemon.json"
        restore_daemon
        exit 1
    fi
    for _ in $(seq 1 30); do
        docker info >/dev/null 2>&1 && break
        sleep 1
    done
    docker info --format '{{json .RegistryConfig.Mirrors}}' || true

    log "4. 验证构建所需基础镜像"
    FAILED=0
    for image in python:3.11-slim node:22-alpine nginx:alpine; do
        echo "拉取 $image ..."
        if timeout 300 docker pull "$image"; then
            echo "OK: $image"
        else
            echo "失败: $image"
            FAILED=1
        fi
    done
    if ((FAILED)); then
        warn "仍有镜像拉取失败，请把上面的输出发给我"
        echo "诊断报告: $REPORT"
        exit 2
    fi
else
    log "3. 没有可用的镜像加速器，测试直连 Docker Hub"
    if ! timeout 120 docker pull hello-world:latest; then
        warn "直连 Docker Hub 也失败，请把上面的输出发给我"
        echo "诊断报告: $REPORT"
        exit 3
    fi
fi

log "5. 完成"
echo "Docker 基础镜像已可拉取。可以回到 GitHub Actions 点击 Re-run failed jobs。"
echo "诊断报告: $REPORT"
