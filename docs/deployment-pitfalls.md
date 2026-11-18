# StockAI 部署踩坑记录

记录 2026-11-18 首次把 StockAI 部署到阿里云 ECS（Alibaba Cloud Linux 3）
并接入个人主页时实际遇到的问题，按“现象 → 原因 → 处理”整理。

## 1. 服务器网络与权限

**服务器访问 `raw.githubusercontent.com` 报 `SSL_ERROR_SYSCALL`**

原因：服务器到 GitHub Raw 的网络链路不可用。

处理：不要在服务器上 `curl` GitHub Raw。所有服务器操作使用可直接粘贴的
自包含脚本，或通过 CI 发布包传输。

**`admin` 用户没有 Mac 登录密码，无法用 `scp` / 交互式 `sudo`**

原因：服务器账号只配置了受限 CI 密钥，没有可供本地登录的密码。

处理：CI 使用专用 ed25519 密钥，`authorized_keys` 中通过 `command=` 绑定
`/usr/local/sbin/stockai-ci-deploy`，只允许：

```text
deploy <sha256> <release-id>
```

发布包从 stdin 传入，脚本校验 SHA256 后交给 `deploy-stockai.sh`。

**CI 用户的主组名不一定等于用户名**

现象：`chown admin:admin` 在部分账号上失败。

处理：统一使用 `id -gn "$DEPLOY_USER"` 读取主组，设置 `.ssh` 和
`authorized_keys` 的属组，不要假设 `user:user`。

## 2. Docker 安装

**`docker` 实际是 Podman 的兼容层**

现象：

```text
Emulate Docker CLI using podman...
podman version 4.9.4-rhel
Failed to enable unit: Unit file docker.service does not exist.
```

原因：系统预装了 `podman-docker`，并残留 `moby-*` 包，导致 Docker CE 无法
正常安装。

处理：移除 `podman-docker` 和冲突的 `moby-*` 包，重新安装 Docker CE、
`docker-buildx-plugin`、`docker-compose-plugin`，确认：

```bash
docker --version
docker compose version
systemctl status docker
```

`docker compose version` 必须使用 Compose v2，而不是 `podman-compose`。

## 3. Docker Hub 与镜像加速器

**`python:3.11-slim` 拉取失败：`insufficient_scope: authorization failed`**

原因：`/etc/docker/daemon.json` 里配置了：

```json
{
  "registry-mirrors": [
    "https://registry.cn-hangzhou.aliyuncs.com",
    "https://mirror.ccs.tencentyun.com"
  ]
}
```

`registry.cn-hangzhou.aliyuncs.com` 是阿里云 ACR，不是 Docker Hub 镜像站，
对 `library/python` 返回 `insufficient_scope`；`mirror.ccs.tencentyun.com`
只适用于腾讯云内网，在阿里云上 DNS 解析失败。Docker 依次尝试这些 mirror
后回退到 `registry-1.docker.io`，而直连 Docker Hub 超时。

处理：不要把 ACR 地址当 mirror。改为实际可用的加速器，并逐个实测：

```json
{
  "registry-mirrors": [
    "https://docker.m.daocloud.io",
    "https://docker.1ms.run"
  ]
}
```

验证命令：

```bash
sudo docker pull python:3.11-slim
sudo docker pull node:22-alpine
sudo docker pull nginx:alpine
```

注意：必须先备份 `/etc/docker/daemon.json`，不要直接覆盖其他 daemon 配置。
判断某个加速器是否可用时，要实际拉取镜像，不能只看 `/v2/` 返回 401/200。

## 4. CI/CD 与发布

**GNU tar 在 `set -o pipefail` 下误报发布包损坏**

现象：`tar -tzf` 在管道中被 `grep` 提前关闭，返回非零退出码。

处理：已改为先生成完整列表再匹配，或在管道中显式处理 SIGPIPE。对应提交
`f26167a fix: avoid GNU tar pipefail false negative in release validation`。

**发布包中不能包含运行时密钥**

`package-release.sh` 会排除 `.env` 和 `python-data-service/config.ini`；
部署脚本解包前再次检查。数据库卷不会在部署时删除，部署失败也会保留旧卷。

**部署在服务器上构建，耗时较长**

`.NET restore/build`、`pip install`、`npm ci` 都在服务器执行，首次发布约
10–12 分钟；部署脚本要求根分区至少有 4 GB 可用空间。当前服务器 40 GB
根分区、发布前约 12 GB 可用。

## 5. 与现有服务共存

服务器已经运行个人主页、NAS、Weekend Merge 等应用：

- `wml.ailoong.net.cn` 通过 Cloudflare Tunnel 指向源站 `https://localhost:443`；
- `nas.ailoong.net.cn` 等域名各自有 Nginx server 块；
- 80/443 由现有 Nginx 统一监听。

StockAI 不应再申请自己的 80/443 入口，也不应覆盖 `/etc/nginx/conf.d` 或
现有 server 块。正确做法：

1. StockAI 只监听 `127.0.0.1:18081`；
2. 在个人主页的 `/etc/nginx/portfolio/routes.conf` 中增加
   `location ^~ /apps/stockai/`；
3. 反向代理到 `http://127.0.0.1:18081/`，由外层 Nginx 负责 TLS。

不要在 `wml.ailoong.net.cn` 上再启动一个占用 80/443 的服务。

## 6. 子路径部署

把 SPA 挂到 `/apps/stockai/` 时，只改 Nginx 不够。以下位置都必须使用
Vite 的 `import.meta.env.BASE_URL`：

- `vite.config.js` 的 `base`；
- Vue Router 的 `createWebHistory(import.meta.env.BASE_URL)`；
- axios 的 `baseURL`；
- `EventSource` 的 SSE 地址。

否则 HTML 仍引用 `/assets/...`，会和主页静态资源冲突；API 也会请求到主页的
`/api/...` 而不是 StockAI。

外层 Nginx 使用带尾斜杠的 `proxy_pass http://127.0.0.1:18081/;` 去掉
`/apps/stockai/` 前缀，StockAI 容器内部仍按根路径运行。不要用
`sub_filter` 重写 HTML，构建时确定 base 更可靠。

SSE 入口 `/apps/stockai/api/watchlist/trading-plan/events` 需要在外层
关闭代理缓冲：

```nginx
proxy_buffering off;
proxy_cache off;
proxy_read_timeout 3600s;
```

## 7. 安全

- StockAI 前端使用 Basic Auth；公网必须走 HTTPS，禁止把
  `127.0.0.1:18081` 直接暴露到公网。
- 部署日志和聊天记录中不要输出 `APP_AUTH_PASSWORD`、TuShare Token、
  SSH 私钥等敏感信息。
- 此前公开过的 TuShare Token 应到 TuShare 控制台撤销并重新生成，只写入
  服务器 `/opt/stockai/python-data-service/config.ini`。
- SQLite 数据卷只在部署脚本中做只读备份，不使用 `docker compose down -v`。

## 8. 验收命令

服务器本机：

```bash
sudo docker compose -f /opt/stockai/docker-compose.yml ps
curl -fsS http://127.0.0.1:18081/health
sudo APP_DIR=/opt/stockai BASE_URL=http://127.0.0.1:18081 \
  bash /opt/stockai/deploy/server/verify-stockai.sh
```

接入个人主页后：

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://wml.ailoong.net.cn/apps/stockai/
curl -fsS https://wml.ailoong.net.cn/apps/stockai/health
curl -s https://wml.ailoong.net.cn/apps/stockai/api/aiprompts \
  -u "$APP_AUTH_USERNAME:$APP_AUTH_PASSWORD"
```

未认证访问 `/apps/stockai/` 返回 401 是预期行为；`/apps/stockai/health`
应返回 `{"status":"OK"}`。
