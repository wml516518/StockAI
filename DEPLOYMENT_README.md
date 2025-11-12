# 股票分析系统阿里云部署指南

## 📦 文件说明

- `deploy-stock-analyse.sh` - 完整部署脚本（所有配置写死在脚本中）
- `deploy-stock-analyse-simple.sh` - 简化部署脚本（使用配置文件）
- `deploy-config.sh` - 配置文件（仅用于简化脚本）

## 🚀 快速开始

### 方法一：使用简化脚本（推荐）

1. **修改配置文件**
```bash
# 编辑配置文件
vi deploy-config.sh
```

修改以下关键配置：
```bash
# GitHub仓库地址 - 替换为你的实际仓库
GITHUB_REPO="https://github.com/your-username/StockAnalyse.git"

# 域名配置 - 如果没有域名，可以设置为服务器IP
DOMAIN_NAME="your-domain.com"

# 是否启用SSL
ENABLE_SSL="false"
```

2. **运行部署脚本**
```bash
# 添加执行权限
chmod +x deploy-stock-analyse-simple.sh

# 运行部署
./deploy-stock-analyse-simple.sh
```

### 方法二：使用完整脚本

1. **直接修改脚本中的变量**
```bash
# 编辑脚本
vi deploy-stock-analyse.sh
```

修改脚本开头的配置变量：
```bash
# 配置变量 - 请根据实际情况修改
GITHUB_REPO="https://github.com/your-username/StockAnalyse.git"
DOMAIN_NAME="your-domain.com"
```

2. **运行部署脚本**
```bash
# 添加执行权限
chmod +x deploy-stock-analyse.sh

# 运行部署
./deploy-stock-analyse.sh
```

## ⚙️ 配置说明

### 基本配置

| 配置项 | 说明 | 示例 |
|--------|------|------|
| `GITHUB_REPO` | GitHub仓库地址 | `https://github.com/your-username/StockAnalyse.git` |
| `DOMAIN_NAME` | 域名 | `stock.yourdomain.com` |
| `SERVER_IP` | 服务器IP（自动获取） | 自动获取 |
| `ENABLE_SSL` | 是否启用HTTPS | `true` 或 `false` |

### SSL配置（可选）

如果启用SSL，需要配置以下项：
```bash
ENABLE_SSL="true"
SSL_CERT_PATH="/etc/ssl/certs/stock-analyse.crt"
SSL_KEY_PATH="/etc/ssl/private/stock-analyse.key"
ADMIN_EMAIL="admin@yourdomain.com"
```

### 端口配置

| 服务 | 默认端口 | 说明 |
|------|----------|------|
| 前端/Nginx | 80 (HTTP), 443 (HTTPS) | Web界面和API |
| 后端API | 5000 | ASP.NET Core API |
| Python数据服务 | 5001 | Flask数据服务 |

## 🔧 部署步骤详解

脚本会自动执行以下步骤：

1. ✅ 系统更新和基础工具安装
2. ✅ 安装.NET 8 SDK
3. ✅ 安装Node.js 18
4. ✅ 安装Python 3.8+
5. ✅ 创建项目目录
6. ✅ 克隆GitHub代码
7. ✅ 前端构建（Vue.js）
8. ✅ 后端发布（ASP.NET Core）
9. ✅ Python服务依赖安装
10. ✅ 数据库配置和初始化
11. ✅ 创建systemd服务
12. ✅ 启动服务
13. ✅ 防火墙配置
14. ✅ 安装和配置Nginx
15. ✅ 启动Nginx

## 🌐 访问地址

部署完成后，可以通过以下地址访问：

### HTTP模式（默认）
- **前端界面**: `http://your-domain.com` 或 `http://server-ip`
- **API文档**: `http://your-domain.com/swagger`
- **健康检查**: `http://your-domain.com/health`

### HTTPS模式（SSL启用）
- **前端界面**: `https://your-domain.com`
- **API文档**: `https://your-domain.com/swagger`
- **健康检查**: `https://your-domain.com/health`

## 🛠️ 维护命令

### 服务管理
```bash
# 查看服务状态
sudo systemctl status stock-backend
sudo systemctl status stock-python
sudo systemctl status nginx

# 重启服务
sudo systemctl restart stock-backend
sudo systemctl restart stock-python
sudo systemctl restart nginx

# 停止服务
sudo systemctl stop stock-backend
sudo systemctl stop stock-python
sudo systemctl stop nginx
```

### 日志查看
```bash
# 查看后端日志
sudo journalctl -u stock-backend -f

# 查看Python服务日志
sudo journalctl -u stock-python -f

# 查看Nginx错误日志
sudo tail -f /var/log/nginx/error.log

# 查看Nginx访问日志
sudo tail -f /var/log/nginx/access.log
```

### 代码更新
```bash
# 进入项目目录
cd /opt/stock-analyse

# 拉取最新代码
git pull

# 重新构建前端
cd frontend
npm install
npm run build
cd ..

# 重新发布后端
cd src/StockAnalyse.Api
dotnet publish -c Release -o /opt/stock-analyse/publish/backend
cd ../..

# 重启服务
sudo systemctl restart stock-backend
sudo systemctl restart nginx
```

## 🔍 故障排除

### 常见问题

1. **服务启动失败**
```bash
# 检查服务状态
sudo systemctl status stock-backend
sudo journalctl -u stock-backend -n 50
```

2. **端口占用**
```bash
# 检查端口使用情况
sudo netstat -tlnp | grep :5000
sudo netstat -tlnp | grep :5001
sudo netstat -tlnp | grep :80
```

3. **Nginx配置错误**
```bash
# 测试配置
sudo nginx -t

# 重新加载配置
sudo nginx -s reload
```

4. **数据库问题**
```bash
# 检查数据库文件权限
ls -la /opt/stock-analyse/publish/backend/stockanalyse.db
```

5. **防火墙问题**
```bash
# 检查防火墙规则
sudo firewall-cmd --list-all

# 添加端口（如果需要）
sudo firewall-cmd --permanent --add-port=80/tcp
sudo firewall-cmd --reload
```

### 健康检查

```bash
# API健康检查
curl http://localhost:5000/health

# Python服务检查
curl http://localhost:5001/health

# 前端检查
curl http://localhost/
```

## 🔒 安全配置

### SSL证书配置

如果需要启用HTTPS，推荐使用Let's Encrypt：

```bash
# 安装Certbot
sudo yum install -y certbot python3-certbot-nginx

# 获取证书
sudo certbot --nginx -d your-domain.com -d www.your-domain.com

# 设置自动续期
sudo crontab -e
# 添加：0 12 * * * /usr/bin/certbot renew --quiet
```

### 防火墙配置

脚本会自动配置基本的防火墙规则。如需自定义：

```bash
# 查看当前规则
sudo firewall-cmd --list-all

# 添加自定义端口
sudo firewall-cmd --permanent --add-port=8080/tcp

# 重新加载
sudo firewall-cmd --reload
```

## 📊 监控和备份

### 服务器监控

```bash
# 系统资源监控
htop
# 或
top

# 磁盘使用情况
df -h

# 服务日志监控
sudo journalctl -u stock-backend -f
```

### 数据备份

```bash
# 备份数据库
cp /opt/stock-analyse/publish/backend/stockanalyse.db /opt/stock-analyse/backup/stockanalyse-$(date +%Y%m%d).db

# 备份配置文件
cp /opt/stock-analyse/src/StockAnalyse.Api/appsettings.json /opt/stock-analyse/backup/

# 备份策略配置
cp -r /opt/stock-analyse/src/StockAnalyse.Api/strategy-configs /opt/stock-analyse/backup/
```

### 自动备份脚本

```bash
# 创建备份脚本
cat > /opt/stock-analyse/backup.sh << 'EOF'
#!/bin/bash
BACKUP_DIR="/opt/stock-analyse/backup"
DATE=$(date +%Y%m%d_%H%M%S)

mkdir -p $BACKUP_DIR

# 备份数据库
cp /opt/stock-analyse/publish/backend/stockanalyse.db $BACKUP_DIR/stockanalyse-$DATE.db

# 备份配置
cp /opt/stock-analyse/src/StockAnalyse.Api/appsettings.json $BACKUP_DIR/

# 清理30天前的备份
find $BACKUP_DIR -name "*.db" -mtime +30 -delete

echo "备份完成: $DATE"
EOF

# 设置定时备份
chmod +x /opt/stock-analyse/backup.sh
echo "0 2 * * * /opt/stock-analyse/backup.sh" | sudo crontab -
```

## 📞 技术支持

如果遇到部署问题，请：

1. 查看服务日志
2. 检查系统资源使用情况
3. 确认网络连接正常
4. 验证配置文件正确性

如需进一步帮助，请提供详细的错误信息和日志。

---

**部署时间**: 约15-30分钟
**维护难度**: 中等
**推荐配置**: 2核4G以上
