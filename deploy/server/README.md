# StockAI 服务器部署脚本

GitHub Actions 自动发布配置见 [CICD.md](CICD.md)。

适用于 Ubuntu、Debian、CentOS、Rocky、AlmaLinux 和 Alibaba Cloud Linux。
保留 Vue + ASP.NET Core + Python 架构，不修改个人主页的运行方式。

使用 GitHub Actions 时不需要先手动部署：配置 CI 密钥后，首次发布由工作流
创建 `/opt/stockai` 并完成构建。下面的手动流程适用于不使用 CI 的场景。

## 脚本

- `package-release.sh`：在本地打包当前工作树，排除 `.env`、`config.ini`、数据库和构建产物。
- `install-docker.sh`：缺失时安装 Docker CE、Buildx 和 Compose v2。
- `deploy-stockai.sh`：检查环境、备份数据库、构建并启动容器，可选配置 Nginx 和 HTTPS。
- `configure-nginx.sh`：单独配置域名入口、SSE 反向代理和 Let's Encrypt。
- `verify-stockai.sh`：验证健康检查、认证、安全响应头、SSE、HTTPS 和容器状态。
- `setup-cicd-user.sh`：新建受限制的 `stockai-ci` 账号，用于 GitHub Actions。
- `setup-cicd-existing-user.sh`：复用现有账号（默认 `root`），不新建用户。

## 1. 本地打包

手动部署时，在本地打包当前工作树，不要直接 `git clone`：

```bash
cd /path/to/StockAI
bash deploy/server/package-release.sh /tmp/stockai-release.tar.gz
```

上传到服务器：

```bash
scp /tmp/stockai-release.tar.gz user@8.148.30.195:/root/
scp /tmp/stockai-release.tar.gz.sha256 user@8.148.30.195:/root/
```

服务器上先校验：

```bash
cd /root
sha256sum -c stockai-release.tar.gz.sha256
```

## 2. 服务器准备

先用 DNS 为 StockAI 添加 A 记录，例如 `stock.example.com -> 8.148.30.195`。
云服务器安全组需要放行 TCP 80 和 443；应用端口 `18081` 只绑定
`127.0.0.1`，不应在安全组开放。

解压安装脚本：

```bash
mkdir -p /root/stockai-install
tar -xzf /root/stockai-release.tar.gz -C /root/stockai-install
```

如果服务器没有 Docker：

```bash
sudo bash /root/stockai-install/deploy/server/install-docker.sh
```

## 3. 部署

```bash
sudo APP_DIR=/opt/stockai \
  STOCKAI_DOMAIN=stock.example.com \
  LETSENCRYPT_EMAIL=you@example.com \
  bash /root/stockai-install/deploy/server/deploy-stockai.sh \
  /root/stockai-release.tar.gz
```

部署默认写入 `admin` / `StockAI@2026`，并覆盖服务器 `.env` 中可能遗留的
临时旧凭据；凭据文件权限为 `600`。也可以设置 `STOCKAI_AUTH_USERNAME` /
`STOCKAI_AUTH_PASSWORD` 覆盖默认值。查看当前用户名和密码：

```bash
sudo grep -E '^APP_AUTH_(USERNAME|PASSWORD)=' /opt/stockai/.env
```

国内网络构建缓慢时，可显式使用已验证的镜像源：

```bash
sudo APP_DIR=/opt/stockai \
  STOCKAI_DOMAIN=stock.example.com \
  LETSENCRYPT_EMAIL=you@example.com \
  NUGET_SOURCE=https://mirrors.huaweicloud.com/repository/nuget/v3/index.json \
  APT_MIRROR=mirrors.aliyun.com \
  bash /root/stockai-install/deploy/server/deploy-stockai.sh \
  /root/stockai-release.tar.gz
```

这些是第三方镜像，使用前应确认组织允许通过该源获取构建依赖；不设置时使用官方源。

## 4. 上线验收

```bash
sudo APP_DIR=/opt/stockai \
  BASE_URL=https://stock.example.com \
  bash /opt/stockai/deploy/server/verify-stockai.sh
```

验收通过后，在个人主页的 Netlify 环境变量中设置：

```text
VITE_STOCKAI_URL=https://stock.example.com
```

然后重新部署个人主页。该地址为空时，主页只显示 StockAI 的 GitHub 和部署状态。

## 更新

1. 本地重新执行 `package-release.sh`。
2. 上传新发布包。
3. 使用相同命令再次运行 `deploy-stockai.sh`。

脚本会复用 `/opt/stockai/.env`、`config.ini` 和 Docker 数据卷。升级前会使用
SQLite 在线备份 API 把现有数据库写入 `/opt/stockai/backups/`。

不要执行 `docker compose down -v`；`-v` 会删除数据库卷。

## Nginx 与 HTTPS

`configure-nginx.sh` 只新增 `/etc/nginx/conf.d/stockai.conf`，不会删除现有站点。
修改前会备份同名配置；`nginx -t` 失败时自动撤销。SSE 路径单独关闭缓冲、
缓存和 gzip，并把认证头原样转发给应用。

如果服务器现有 Nginx 配置本身就无法通过 `nginx -t`，脚本会安全退出。应先修复
原有的 500 错误，再部署 StockAI。

证书申请要求域名已经解析到服务器，且公网可访问 80/443。未设置
`LETSENCRYPT_EMAIL` 时只配置 HTTP；登录密码不应通过明文 HTTP 暴露到公网。

## 凭据与数据

- Web 登录凭据：`/opt/stockai/.env`；未配置时为 `admin` / `StockAI@2026`。
- Python 数据源配置：`/opt/stockai/python-data-service/config.ini`。
- SQLite 数据卷：`stockai_stock-data`。
- 数据库备份：`/opt/stockai/backups/`。
- Nginx 配置备份：`/etc/nginx/conf.d/stockai.conf.bak.<时间>`。

此前提交到 Git 的 TuShare Token 仍应撤销并重新生成；新 Token 只写入服务器
`config.ini`，不要提交或打进发布包。
