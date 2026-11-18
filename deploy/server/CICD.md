# GitHub Actions CI/CD

工作流文件：`.github/workflows/stockai.yml`。

## 流程

- Pull Request、`main`/`master` 推送：运行后端构建、后端与部署脚本回归、
  前端构建、ShellCheck、Compose 配置检查、发布包检查，并构建三个 Docker 镜像。
- `v*` 标签：在全部检查通过后生成固定提交的发布包，进入 `production`
  Environment 审批，审批通过后上传并部署。
- 手动 `workflow_dispatch`：可选择 `deploy=true`，同样需要 Environment 审批。
- 部署任务串行执行，`cancel-in-progress: false`，不会并发覆盖。

CI 不会在普通主分支推送时直接修改生产服务器。

## 一次性服务器配置

### 1. 先生成 CI 密钥

在可信的本地电脑执行，不要把私钥上传到服务器：

```bash
ssh-keygen -t ed25519 -C "stockai-ci" \
  -f ~/.ssh/stockai-ci -N ""
```

只上传公钥：

```bash
scp ~/.ssh/stockai-ci.pub user@8.148.30.195:/root/
```

如果已经有一把**专用部署密钥**，可以跳过生成步骤，直接使用它的公钥和私钥。
不要复用日常登录密钥：强制命令只对匹配的密钥条目生效，而日常密钥通常已有
不受限条目。

### 2. 安装 CI 部署入口

不需要先手动部署 StockAI。CI 会把发布包上传给部署器，由部署器在首次
发布时创建 `/opt/stockai` 并完成构建。

把三个脚本上传到服务器的临时目录：

```bash
scp deploy/server/setup-cicd-existing-user.sh \
  deploy/server/ci-deploy-wrapper.sh \
  deploy/server/install-docker.sh \
  user@8.148.30.195:/tmp/
```

服务器上执行：

```bash
sudo bash /tmp/setup-cicd-existing-user.sh /root/stockai-ci.pub
```

脚本会：

- 复用现有账号（默认 `root`），不创建用户、不修改密码。
- 缺少 Docker/Compose v2 时自动运行同目录的 `install-docker.sh`。
- 安装 `/usr/local/sbin/stockai-ci-deploy`。
- 创建 root 所有、权限 `600` 的 `/etc/stockai/deploy.env`。
- 给 CI 公钥加受限强制命令，只允许执行部署器。

第一次 CI 发布会自动创建 `/opt/stockai`。如果该目录已存在且不是本脚本
管理的目录，部署会安全退出；此时可在 `/etc/stockai/deploy.env` 中把
`APP_DIR` 改成新的空目录。

指定已有普通用户时：

```bash
sudo DEPLOY_USER=ubuntu \
  bash /tmp/setup-cicd-existing-user.sh \
  /root/stockai-ci.pub
```

普通用户会自动新增仅允许执行部署器的 sudoers 规则；`root` 不需要。
GitHub Environment 中的 `DEPLOY_USER` 填实际复用的账号。

注意：

- 仍然建议使用一把**专用 CI 密钥**，不要复用日常登录的私钥。
- 如果该公钥已经以不受限方式存在于目标账号的 `authorized_keys`，
  脚本会拒绝继续。因为 OpenSSH 可能先匹配到旧条目，旧条目会让新限制失效。
  此时应换一把专用公钥，或手动移除旧的同密钥条目。
- 复用 `root` 不会改变现有登录方式，但 CI 密钥本身仍被限制为只能执行
  `deploy <sha256> <release-id>`。

### 可选：新建独立 stockai-ci 账号

如果希望使用独立账号而不是复用现有账号：

```bash
sudo STOCKAI_DOMAIN=stock.example.com \
  LETSENCRYPT_EMAIL=you@example.com \
  bash /tmp/setup-cicd-user.sh \
  /root/stockai-ci.pub
```

脚本会：

- 创建 `stockai-ci` 用户并锁定密码登录。
- 缺少 Docker/Compose v2 时自动运行同目录的 `install-docker.sh`。
- 安装 `/usr/local/sbin/stockai-ci-deploy`。
- 将公钥限制为只能通过 sudo 执行该脚本，不能开 Shell、转发端口或复制文件。
  原始 SSH 命令会作为单个位置参数传给执行器，不依赖 `sudo` 保留环境变量。
- 创建 root 所有、权限 `600` 的 `/etc/stockai/deploy.env`。

生成的 `authorized_keys` 行为：

```text
restrict,command="sudo -n /usr/local/sbin/stockai-ci-deploy \"$SSH_ORIGINAL_COMMAND\"" ssh-ed25519 ...
```

`$SSH_ORIGINAL_COMMAND` 在登录 Shell 中展开，并作为单个参数传给执行器。
执行器仍会用严格正则校验 `deploy <sha256> <release-id>`，不会执行其中的
Shell 元字符。

如果之前用旧版脚本配置过，重新运行 `setup-cicd-user.sh` 即可覆盖
`authorized_keys` 和 sudoers 规则。

检查配置：

```bash
sudo cat /etc/stockai/deploy.env
sudo stat -c '%U:%G %a' /etc/stockai/deploy.env
sudo tail -50 /var/log/stockai-ci-deploy.log
```

如果域名、邮箱或构建镜像源需要调整，修改：

```bash
sudoedit /etc/stockai/deploy.env
```

该文件必须保持 `root:root 600`，否则部署执行器会拒绝运行。

### 4. 获取服务器 SSH Host Key

在本地执行：

```bash
ssh-keyscan -p 22 8.148.30.195 > ~/stockai-known-hosts
```

建议同时核对其指纹：

```bash
ssh-keygen -lf ~/stockai-known-hosts
```

服务器上可用下面的命令查看实际指纹并人工比对：

```bash
ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
```

## GitHub 配置

在仓库 `Settings > Environments` 创建 `production`：

- 开启 Required reviewers，至少指定一个审批人。
- 可限制只允许受保护分支或标签部署。

在 `production` Environment 中设置 Secrets：

| Secret | 内容 |
| --- | --- |
| `DEPLOY_HOST` | `8.148.30.195` |
| `DEPLOY_PORT` | SSH 端口，例如 `22` |
| `DEPLOY_USER` | `stockai-ci` |
| `DEPLOY_SSH_KEY` | 本地 `~/.ssh/stockai-ci` 私钥全文 |
| `DEPLOY_KNOWN_HOSTS` | 本地 `~/stockai-known-hosts` 内容 |

设置 Environment Variable：

| Variable | 内容 |
| --- | --- |
| `STOCKAI_PUBLIC_URL` | `https://stock.example.com` |

推荐把这些值放在 `production` Environment，而不是仓库级 Secrets。
这样审批和部署密钥只对生产任务可见。

私钥只应存在于本地受保护位置和 GitHub Environment Secret，不上传服务器、
不提交 Git、不写入日志。

## 触发发布

首次需要先把当前代码提交并推送：

```bash
git add .
git commit -m "feat: add CI/CD and production deployment safeguards"
git push origin master
```

创建版本标签：

```bash
git tag v1.0.0
git push origin v1.0.0
```

标签名会作为发布编号传给服务器，必须匹配
`[A-Za-z0-9._-]{1,64}`，例如 `v1.0.0`、`v1.1.0-rc1`。

工作流完成 CI 后会在 `production` 环境等待审批。审批通过后：

1. 校验发布包 SHA256。
2. 通过受限 SSH 强制命令上传到服务器。
3. 服务器再次校验 SHA256 和发布编号。
4. 获取部署锁，备份 SQLite。
5. 构建、启动容器并等待健康检查。
6. 记录发布编号到 `/opt/stockai/.stockai-managed`。

## 回滚

数据卷不会被删除。回滚代码时可重新运行旧提交对应的 GitHub Actions 发布，
或对旧提交创建新的版本标签。数据库升级后若旧版本不再兼容，应先从
`/opt/stockai/backups/` 选择升级前备份并停止服务后恢复。

当前版本不会自动回滚数据库；自动代码回滚也应在确认数据库兼容后进行。

## 排查

```bash
sudo tail -100 /var/log/stockai-ci-deploy.log
cd /opt/stockai
sudo docker compose ps
sudo docker compose logs --tail=100 backend python-service frontend
sudo cat /opt/stockai/.stockai-managed
```

常见失败原因：

- GitHub Environment 未设置审批人或缺少 Secret。
- `DEPLOY_KNOWN_HOSTS` 与服务端 Host Key 不一致。
- 部署报“仅允许: deploy ...”且命令格式正确：服务器的 `authorized_keys`
  还是旧版规则，重新运行 `setup-cicd-user.sh`。
- `/etc/stockai/deploy.env` 权限不是 `root:root 600`。
- 已有部署任务持有 `/run/stockai-ci-deploy.lock`。
- 发布包 SHA256 不一致或超过 200 MB。
- 服务器 Docker 构建缺少网络、磁盘空间或镜像源不可达。
