#!/usr/bin/env bash
set -Eeuo pipefail

die() {
    echo "错误: $*" >&2
    exit 1
}

[[ "${EUID}" -eq 0 ]] || die "请使用 root 或 sudo 运行"

if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    echo "Docker 和 Docker Compose 已安装，无需重复安装。"
    docker --version
    docker compose version
    exit 0
fi

if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091
    . /etc/os-release
else
    die "无法识别操作系统"
fi

echo "即将安装 Docker CE 与 Compose 插件: ${PRETTY_NAME:-$ID}"

case "${ID:-}" in
    ubuntu|debian)
        export DEBIAN_FRONTEND=noninteractive
        apt-get update
        apt-get install -y ca-certificates curl gnupg
        install -m 0755 -d /etc/apt/keyrings
        curl -fsSL "https://download.docker.com/linux/${ID}/gpg" \
            -o /etc/apt/keyrings/docker.asc
        chmod a+r /etc/apt/keyrings/docker.asc
        printf '%s\n' \
            "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/${ID} ${VERSION_CODENAME} stable" \
            > /etc/apt/sources.list.d/docker.list
        apt-get update
        apt-get install -y \
            docker-ce docker-ce-cli containerd.io \
            docker-buildx-plugin docker-compose-plugin
        ;;
    centos|rhel|rocky|almalinux|alinux|fedora)
        if command -v dnf >/dev/null 2>&1; then
            dnf install -y dnf-plugins-core
            dnf config-manager --add-repo \
                https://download.docker.com/linux/centos/docker-ce.repo
            dnf install -y \
                docker-ce docker-ce-cli containerd.io \
                docker-buildx-plugin docker-compose-plugin
        elif command -v yum >/dev/null 2>&1; then
            yum install -y yum-utils
            yum-config-manager --add-repo \
                https://download.docker.com/linux/centos/docker-ce.repo
            yum install -y \
                docker-ce docker-ce-cli containerd.io \
                docker-buildx-plugin docker-compose-plugin
        else
            die "未找到 dnf 或 yum"
        fi
        ;;
    *)
        die "暂不支持自动安装 ${ID:-unknown}，请先按 Docker 官方文档安装"
        ;;
esac

systemctl enable --now docker
docker --version
docker compose version

if [[ -n "${SUDO_USER:-}" && "${SUDO_USER}" != "root" ]]; then
    usermod -aG docker "$SUDO_USER"
    echo "已将 ${SUDO_USER} 加入 docker 组；重新登录后生效。"
fi

echo "Docker 安装完成。"
