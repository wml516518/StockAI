# Docker 部署指南

## 快速开始

### 1. 前置要求

- Docker 20.10+
- Docker Compose 2.0+
- Git

### 2. 配置环境变量

```bash
# 复制环境变量模板
cp .env.example .env

# 编辑.env文件，填写必要的配置
nano .env
```

必填项:
- `GEMINI_API_KEY`: 你的Gemini API密钥

### 3. 启动服务

```bash
# 构建并启动所有服务
docker-compose up -d

# 查看服务状态
docker-compose ps

# 查看日志
docker-compose logs -f
```

### 4. 访问应用

- 前端: http://localhost
- 后端API: http://localhost/api
- Python服务: 内部服务，通过后端调用

## 服务管理

### 查看服务状态
```bash
docker-compose ps
```

### 查看日志
```bash
# 所有服务
docker-compose logs -f

# 特定服务
docker-compose logs -f backend
docker-compose logs -f frontend  
docker-compose logs -f python-service
```

### 重启服务
```bash
# 重启所有服务
docker-compose restart

# 重启特定服务
docker-compose restart backend
```

### 停止服务
```bash
docker-compose stop
```

### 停止并删除容器
```bash
docker-compose down
```

### 停止并删除容器和数据卷
```bash
docker-compose down -v
```

## 更新部署

```bash
# 拉取最新代码
git pull

# 重新构建并启动
docker-compose up -d --build

# 清理未使用的镜像
docker image prune -f
```

## CI/CD自动部署

### GitHub Actions配置

1. **设置GitHub Secrets**

在GitHub仓库设置中添加以下secrets:
- `SERVER_HOST`: 服务器IP或域名
- `SERVER_USER`: SSH用户名
- `SERVER_SSH_KEY`: SSH私钥
- `SERVER_PORT`: SSH端口（可选，默认22）

2. **触发部署**

- 推送代码到main/master分支会自动触发部署
- 也可以在GitHub Actions页面手动触发

### 工作流程

1. 代码推送到GitHub
2. GitHub Actions自动构建Docker镜像
3. 镜像推送到GitHub Container Registry
4. SSH连接到服务器
5. 拉取最新镜像并重启服务

## 数据持久化

数据存储在以下目录:
- `./data`: 数据库文件
- `./logs`: 应用日志

**重要**: 请定期备份这些目录！

## 健康检查

每个服务都配置了健康检查:

```bash
# 检查服务健康状态
docker-compose ps

# 查看详细健康检查信息
docker inspect stock-api | grep -A 10 Health
```

## 性能优化

### 资源限制

在`docker-compose.yml`中添加资源限制:

```yaml
services:
  backend:
    deploy:
      resources:
        limits:
          cpus: '2'
          memory: 2G
        reservations:
          cpus: '0.5'
          memory: 512M
```

### 日志轮转

配置日志驱动避免日志文件过大:

```yaml
services:
  backend:
    logging:
      driver: "json-file"
      options:
        max-size: "10m"
        max-file: "3"
```

## 故障排查

### 容器无法启动

```bash
# 查看详细日志
docker-compose logs backend

# 检查配置
docker-compose config
```

### 网络问题

```bash
# 查看网络
docker network ls
docker network inspect stock-network

# 重建网络
docker-compose down
docker network prune
docker-compose up -d
```

### 数据库问题

```bash
# 进入容器检查
docker exec -it stock-api sh
ls -la /app/data

# 从旧部署迁移数据库
cp /opt/stock-analyse/data/stockanalyse.db ./data/
```

## 从传统部署迁移

1. **备份现有数据**
```bash
# 备份数据库
cp /opt/stock-analyse/data/stockanalyse.db ./backup/

# 备份配置
cp /opt/stock-analyse/python-data-service/config.ini ./backup/
```

2. **停止旧服务**
```bash
sudo systemctl stop stock-analyse-api
sudo systemctl stop stock-analyse-python
sudo systemctl stop nginx
```

3. **启动Docker服务**
```bash
# 复制数据库到新位置
mkdir -p ./data
cp ./backup/stockanalyse.db ./data/

# 启动服务
docker-compose up -d
```

4. **验证**
```bash
# 检查服务状态
docker-compose ps

# 测试前端
curl http://localhost

# 测试API
curl http://localhost/api/health
```

## 安全建议

1. **使用环境变量管理敏感信息**
   - 不要提交`.env`文件到Git
   - 生产环境使用更强的密钥

2. **启用HTTPS**
   - 使用Nginx反向代理配置SSL
   - 推荐使用Let's Encrypt免费证书

3. **定期更新镜像**
```bash
docker-compose pull
docker-compose up -d
```

4. **限制容器权限**
   - 已配置非root用户运行
   - 考虑使用SELinux或AppArmor

## 监控

### 查看资源使用
```bash
docker stats
```

### 导出容器日志
```bash
docker-compose logs --no-color > app.log
```

## 支持与反馈

遇到问题请查看:
- GitHub Issues
- 日志文件: `./logs/`
- Docker日志: `docker-compose logs`
