# Docker 部署与本机验收

保留 Vue + ASP.NET Core + Python 的架构。当前配置供本机验收或服务器内部运行，
默认只发布 `127.0.0.1:18081`，后端和 Python 不发布宿主机端口。
整站已启用 Basic Auth，模型 API Key 不再返回浏览器；公网入口仍必须配置 HTTPS。
这份指南不代表已经部署到服务器或集成个人主页。

服务器由你本人操作时，可直接使用 `deploy/server/` 下的发布、部署、Nginx 和验收脚本；
具体命令与前置条件见 [deploy/server/README.md](deploy/server/README.md)。
GitHub Actions 的测试、审批和自动发布流程见
[deploy/server/CICD.md](deploy/server/CICD.md)。当前工作树尚未提交到 GitHub，
因此手工部署时应先打包再上传，不能直接依赖远端仓库。

## 环境与启动

- Docker Engine 20.10 或更高版本。
- Docker Compose 2.18 或更高版本，支持 `up --wait --wait-timeout`。
- `python-data-service/config.ini` 必须是实际文件。首次使用时从模板复制，
  TuShare Token 可留空；部分数据需要数据源权限。
- `.env` 必须设置非空的 `APP_AUTH_USERNAME` 和 `APP_AUTH_PASSWORD`。

可用下面的命令生成随机密码，再把输出写入 `.env`：

```bash
printf 'APP_AUTH_PASSWORD=%s\n' "$(openssl rand -hex 24)"
```

在仓库根目录执行：

```bash
test -f .env || cp .env.example .env
test -f python-data-service/config.ini \
  || cp python-data-service/config.example.ini python-data-service/config.ini
# 编辑 .env，设置至少 20 位的随机 APP_AUTH_PASSWORD
docker compose config --quiet
docker compose up -d --build --wait --wait-timeout 180
docker compose ps
```

也可以运行 `bash docker-deploy.sh`。脚本会创建缺失的 `.env`，等待全部服务健康后才报告成功。
通过 `.env` 的 `HTTP_PORT` 修改本机端口，保留 `HTTP_BIND_ADDRESS=127.0.0.1`。

访问入口：

- 前端：`http://127.0.0.1:18081/`
- 应用健康检查：`http://127.0.0.1:18081/health`
- 业务 API：`http://127.0.0.1:18081/api/`

打开前端或业务 API 时会要求输入 `.env` 中的用户名和密码。健康检查不加认证，
只返回数据库连接状态，供容器和反向代理探活。

## 访问保护与凭据

- 前端 Nginx 和后端都会校验同一组 Basic Auth 凭据，静态页面、API 和 SSE 均受保护。
- Basic Auth 只能通过 HTTPS 暴露到公网；明文 HTTP 会传输可直接复用的凭据。
- 外层宝塔/Nginx 必须保留 `Authorization` 和 `Host` 请求头，并设置正确的
  `X-Forwarded-Proto`；不能缓存带认证的响应。后端用 `Host` 校验写请求的
  `Origin`，防止跨站利用浏览器缓存的 Basic Auth 凭据。
- `/api/aimodelconfig` 只返回 `hasApiKey`，不返回密钥原文。编辑时密钥留空表示保留服务端旧值；
  “测试”按钮通过配置 ID 使用服务端密钥，不需要把密钥回传到浏览器。
- 模型密钥仍保存在 SQLite 数据库中，数据库卷和备份都应按敏感数据处理。
- TuShare Token 放在已被 Git 忽略的 `python-data-service/config.ini`，
  该文件的模板是 `config.example.ini`。

## 服务连接与健康检查

后端使用 `PYTHON_DATA_SERVICE_URL=http://python-service:5001` 调用 Python。
各服务按 Python 健康 → 后端健康 → 前端健康的顺序启动。

```bash
curl -fsS http://127.0.0.1:18081/health
docker compose exec -T backend curl -fsS http://python-service:5001/health
curl -u '用户名:密码' -fsS http://127.0.0.1:18081/api/aiprompts
curl -u '用户名:密码' -N --max-time 3 \
  http://127.0.0.1:18081/api/watchlist/trading-plan/events
```

最后一条命令应立即输出 `connected` 事件，随后因人为设置的三秒超时而退出。
前端 `/health` 代理到后端数据库连接检查，不再返回固定成功文本。
Python `/health` 证明进程能接收请求，不代表外部行情网站可用。

SSE 路径关闭 Nginx 缓冲、缓存和 gzip，允许长连接。服务器外层宝塔/Nginx
反向代理也必须对该路径关闭缓冲，并设置足够的读取超时。

## 数据持久化与备份

数据库位于容器 `/app/data/stockanalyse.db`，由 `stock-data` 命名卷保存。
后端和 Python 的日志目录分别使用 `stock-backend-logs`、`stock-python-logs` 命名卷。
标准仓库目录名为 `StockAI` 时，实际数据库卷名为 `stockai_stock-data`；
若使用 `docker compose -p NAME`，卷名前缀也会改变。

重建容器或 `docker compose down` 不删除命名卷。`docker compose down -v`
会删除数据，不能作为常规更新步骤。

使用 SQLite 在线备份 API 生成一致的备份，避免直接复制仍在写入的数据库：

```bash
mkdir -p backups
docker run --rm \
  -v stockai_stock-data:/source \
  -v "$PWD/backups:/backup" \
  python:3.11-slim python -c \
  'import datetime, sqlite3; source = sqlite3.connect("/source/stockanalyse.db"); target = sqlite3.connect("/backup/stockanalyse-" + datetime.date.today().isoformat() + ".db"); source.backup(target); target.close(); source.close()'
```

旧版使用 `./data` 或工作目录中的 `stockanalyse.db`，不会自动迁移到新卷。
切换前应确认实际数据库位置，通过 SQLite backup API 导出一致副本，停止旧服务，
再把该副本导入新卷。恢复时必须停止新服务，并保证数据库文件由 UID/GID `1000:1000`
拥有；不要覆盖正在使用的数据库，也不要保留其他数据库对应的旧 WAL/SHM 文件。

## 更新与排错

```bash
docker compose up -d --build --wait --wait-timeout 180
docker compose logs --tail=100 backend python-service frontend
docker compose ps
```

数据库迁移失败会阻止后端启动，避免把迁移失败伪装成健康状态。
应先查看日志、备份并检查现有数据库，再解决迁移问题。

后端现已使用 .NET 10 与 EF Core 10；升级前先备份数据库。启动时会检查迁移，
不要在数据库迁移失败时强行切换入口或删除数据库重试。

GitHub Actions 的 `Docker Build and Validate` 工作流先构建 .NET 10 后端并执行部署、
访问保护回归，再构建三个服务镜像。PR 只验证构建，不登录 GHCR 或推送镜像；
主分支推送会发布分支标签、完整提交 SHA 标签及默认分支的 `latest` 标签。
这份工作流不再自动修改服务器，也不会执行 `down` 或全局镜像清理。
目前 Compose 从源码构建，不会使用 GHCR 的标签；服务器部署请使用本指南中的
`docker compose up -d --build --wait --wait-timeout 180`，以实际健康检查作为成功条件。
正式上线仍需确认服务器 CPU 架构、SSH 访问方式、HTTPS 域名和现有 Nginx 配置。

`deploy-stock-analyse.sh` 与 `deploy-stock-analyse-simple.sh` 是未适配当前架构配置的
旧版裸机脚本，已停用；不要按 `DEPLOYMENT_README.md` 中的旧步骤更新系统。

## 已公开凭据处置

`python-data-service/config.ini` 曾被 Git 跟踪，其中的 TuShare Token 应视为已经公开。
本轮已停止跟踪该文件并增加配置模板，但 Git 历史和 GitHub 历史不会因此自动清除。
请先到 TuShare 控制台撤销并重新生成 Token，再把新 Token 写入本机 `config.ini`；
仅删除文件或改写 `.gitignore` 不能使旧 Token 失效。

## 回归验证

有 .NET 10 SDK 和运行时的环境中执行：

```bash
dotnet build src/StockAnalyse.Api/StockAnalyse.Api.csproj
python3 tests/test_api_deployment.py
python3 tests/test_api_security.py
```

测试启动隔离的后端实例，验证健康接口、配置指定的数据库路径、自动创建数据目录，
以及更换工作目录后保留已写入的数据；安全测试验证未认证拒绝、密钥不回传、
空密钥更新保留原值和按配置 ID 测试连接。也可通过 `STOCKAI_TEST_API_DLL` 指定构建产物。
这些测试使用本机假模型服务，不调用外部行情网站或收费模型 API。

本机 Compose 全部健康后执行 `python3 tests/test_compose_deployment.py`，检查网页、
API、Python 内网连接、SSE 首包，以及重建后端容器后的数据保留。
该测试会重建本机后端容器，并创建后删除一条临时提示词；只用于本机验收环境。

网络较慢时，可以指定已缓存或内部镜像仓库提供的 SDK 镜像，并覆盖 NuGet、Ubuntu 软件源：

```bash
docker compose build backend \
  --build-arg DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0 \
  --build-arg NUGET_SOURCE=https://mirrors.huaweicloud.com/repository/nuget/v3/index.json \
  --build-arg APT_MIRROR=mirrors.aliyun.com
```

`NUGET_SOURCE` 默认是 `https://api.nuget.org/v3/index.json`，`APT_MIRROR` 默认为空，
即使用运行时镜像自带的 Ubuntu 源。可用的镜像地址应以当前网络环境实际验证为准。

构建镜像的 CPU 架构应与目标服务器一致；`DOCKER_DEFAULT_PLATFORM=linux/amd64`
可用于在 ARM 开发机上验证常见的 x86 云服务器环境。
