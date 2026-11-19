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
2. 在个人主页 `/etc/nginx/conf.d/wml.ailoong.net.cn.conf` 的 443 server
   块中 include `/etc/nginx/snippets/stockai.conf`，由 snippet 提供
   `location ^~ /apps/stockai/`；
3. 反向代理到 `http://127.0.0.1:18081/`，由外层 Nginx 负责 TLS。

不要在 `wml.ailoong.net.cn` 上再启动一个占用 80/443 的服务。

仓库文档里的 `/etc/nginx/portfolio/routes.conf` 是合并参考；这台服务器实际
把 `wml` 的 location 拆在 `/etc/nginx/conf.d/wml.ailoong.net.cn.conf` 和
`/etc/nginx/snippets/*.conf`，以 `nginx -T` 的实际输出为准。

`systemctl reload nginx` 后旧 worker 仍可能短暂服务请求，紧接着的健康检查
偶尔会先看到一次 404；重试一次即可，不要据此重复改配置。

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

## 7. 运行时配置

**AI 模型的占位 Key 被误判为“已配置”**

现象：首次部署后，设置页显示 API Key 已保存，但实际没有填写；执行 AI
分析时请求上游返回 401。

原因：旧版 `DatabaseSeeder` 会写入 `ApiKey = "your-api-key-here"`，设置接口
只判断 Key 是否非空，因此把占位值当成了真实密钥。

处理：`v1.0.2` 起不再写入占位 Key，启动时会清理数据库中旧的
`your-api-key-here`，并在返回 `hasApiKey` 时排除该占位值。升级后应在
“设置 → AI 模型配置”中重新保存真实 Key。不要通过数据库手工写入示例 Key。

**AI 端点必须是 OpenAI Chat Completions 兼容格式**

设置页中的端点必须能够接收 OpenAI 兼容请求，即路径以
`/v1/chat/completions` 结尾。常见配置：

```text
DeepSeek
端点: https://api.deepseek.com/v1/chat/completions
模型: deepseek-chat

阿里云百炼（DashScope 兼容模式）
端点: https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions
模型: qwen-plus 或 qwen-max
```

不要填写 DashScope 原生接口或只有域名/Base URL 的地址。API Key 保存在
SQLite 中，接口只返回 `hasApiKey`，不会把密钥原文回传到浏览器。

**TuShare Token 修改后必须重启 Python 服务**

生产配置位于：

```text
/opt/stockai/python-data-service/config.ini
```

文件格式：

```ini
[TuShare]
Token = 在服务器上填入真实 Token
```

该文件以只读方式挂载到容器的 `/app/config.ini`，Python 服务在启动时读取，
因此修改后需要重启：

```bash
cd /opt/stockai
sudo docker compose restart python-service
sudo docker compose logs --tail=100 python-service
```

不要在终端使用 `echo` 输出 Token，也不要把它提交到 Git 或发到聊天。
`stock_basic` 等接口通常需要约 2000 积分；积分不足只代表该接口权限不够，
不代表 Token 格式错误。其他行情与行业数据路径仍可使用 AKShare/EastMoney，
不要只凭 `stock_basic` 失败就认定 TuShare Token 不可用。

**登录会话与代理头**

当前临时默认账号为 `admin` / `StockAI@2026`。首次部署和未配置环境变量时
使用这组值；已有 `/opt/stockai/.env` 或显式环境变量仍优先：

```bash
sudo grep -E '^APP_AUTH_(USERNAME|PASSWORD)=' /opt/stockai/.env
```

登录页通过后端签发的 HttpOnly 会话 Cookie 保持登录状态。外层 Nginx 必须
转发 `Cookie` 和 `X-Forwarded-Proto`，否则登录成功后 API 仍会返回 401，
或在 HTTPS 下生成不正确的 Cookie。会话签名密钥保存在数据卷的 `auth-keys`
目录，后端容器重建后仍可继续使用现有会话。公网访问必须使用 HTTPS。

## 8. 安全

- StockAI 前端登录页公开，业务 API 使用会话 Cookie；公网必须走 HTTPS，
  禁止把 `127.0.0.1:18081` 直接暴露到公网。
- `admin` / `StockAI@2026` 写在公开仓库中，只能作为当前阶段的基础访问门槛。
  网站受众扩大前，应修改服务器 `.env` 中的凭据并重新启动后端。
- 部署日志和聊天记录中不要输出 `APP_AUTH_PASSWORD`、TuShare Token、
  SSH 私钥等敏感信息。
- 不要把 SSH 私钥存到 GitHub Variables；Variables 是明文可读的，必须使用
  Environment/Repository Secrets。2026-11-18 已删除 StockAI 仓库中 2025 年
  遗留的 `SERVER_SSH_KEY` 变量；如果服务器 root 的 `authorized_keys` 仍保留
  对应旧 key，应确认不再使用后移除并轮换。
- 此前公开过的 TuShare Token 应到 TuShare 控制台撤销并重新生成，只写入
  服务器 `/opt/stockai/python-data-service/config.ini`。
- SQLite 数据卷只在部署脚本中做只读备份，不使用 `docker compose down -v`。

## 9. 验收命令

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
curl -s -o /dev/null -w '%{http_code}\n' \
  https://wml.ailoong.net.cn/apps/stockai/api/aiprompts
```

未登录访问 `/apps/stockai/` 返回 200 并显示登录页；未登录访问业务 API
返回 401。`/apps/stockai/health` 应返回 `{"status":"healthy"}`。完整登录、
SSE 和退出检查使用 `deploy/server/verify-stockai.sh`，避免在命令行或聊天中
输出密码。
