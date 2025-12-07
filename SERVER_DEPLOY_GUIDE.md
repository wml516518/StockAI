# 服务器部署步骤指南

## 当前状态
✅ Podman环境已安装完成
✅ Docker Compose已安装

## 接下来的部署步骤

### 步骤1: 创建项目目录
```bash
sudo mkdir -p /opt/stock-analyse
sudo chown -R $(whoami):$(whoami) /opt/stock-analyse
cd /opt/stock-analyse
```

### 步骤2: 克隆代码
```bash
# 如果服务器没有Git，先安装
sudo dnf install -y git

# 克隆你的GitHub仓库（替换成实际地址）
git clone https://github.com/你的用户名/StockAI.git .

# 如果是私有仓库或需要认证
# git clone https://<TOKEN>@github.com/你的用户名/StockAI.git .
```

**⚠️ 重要**：代码需要先推送到GitHub！

在本地Windows执行：
```bash
cd e:\TraeDemo\StockAI
git add .
git commit -m "Add Docker deployment files"
git push origin main
```

### 步骤3: 配置环境变量
```bash
# 复制环境变量模板
cp .env.example .env

# 编辑配置
nano .env
```

在编辑器中填写：
```
GEMINI_API_KEY=你的Gemini API密钥
GEMINI_MODEL=gemini-2.0-flash-exp
HTTP_PORT=80
TZ=Asia/Shanghai
```

保存：`Ctrl+X` → `Y` → `Enter`

### 步骤4: 创建数据目录
```bash
mkdir -p data logs/backend logs/frontend logs/python
```

### 步骤5: 安装Docker Compose
```bash
sudo pip3 install docker-compose
docker-compose --version
```

### 步骤6: 构建并启动服务
```bash
# 构建镜像（首次需要10-15分钟）
docker-compose build

# 启动所有服务
docker-compose up -d

# 查看服务状态
docker-compose ps

# 查看日志
docker-compose logs -f
```

### 步骤7: 配置防火墙
```bash
# 开放80端口
sudo firewall-cmd --permanent --add-port=80/tcp
sudo firewall-cmd --reload
```

### 步骤8: 配置阿里云安全组
1. 登录阿里云控制台
2. ECS → 实例 → 找到你的服务器
3. 更多 → 网络和安全组 → 安全组配置
4. 配置规则 → 添加入方向规则
5. 端口范围：`80/80`，授权对象：`0.0.0.0/0`

### 步骤9: 访问测试
浏览器访问：
```
http://你的服务器公网IP
```

## 常用命令

```bash
# 查看日志
docker-compose logs -f

# 重启服务
docker-compose restart

# 停止服务
docker-compose stop

# 更新部署
cd /opt/stock-analyse
git pull
docker-compose up -d --build
```

## 故障排查

### 服务无法启动
```bash
docker-compose logs backend
docker-compose ps
```

### 端口已被占用
```bash
sudo lsof -i :80
sudo netstat -tulpn | grep :80
```

### 数据库权限问题
```bash
sudo chown -R 1000:1000 ./data
chmod 644 ./data/*.db
```
