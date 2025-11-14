# ====================================================
# 股票分析系统部署配置文件
# 请修改以下变量为你的实际配置
# ====================================================

# GitHub仓库地址 - 请替换为你的实际仓库地址
GITHUB_REPO="https://gitclone.com/github.com/wml516518/StockAI.git"
# Git 分支 - 默认 master
GIT_BRANCH="master"

# Git分支 - 指定要克隆的分支
GIT_BRANCH="master"

# 域名配置 - 如果没有域名，可以设置为服务器IP
DOMAIN_NAME="8.148.30.195"

# 服务器IP - 如果上面设置了域名，这里可以留空
# 如果没有域名，这里会自动获取公网IP
SERVER_IP=""

# 数据库配置 - 默认使用SQLite，无需修改
DATABASE_PATH="/opt/stock-analyse/data/stockanalyse.db"

# 服务端口配置 - 如需修改，请同时修改Nginx配置
BACKEND_PORT="5000"
PYTHON_PORT="5001"
HTTP_PORT="80"
HTTPS_PORT="443"

# 是否启用SSL (true/false)
ENABLE_SSL="false"

# SSL证书路径 (如果启用SSL，需要配置)
SSL_CERT_PATH="/etc/ssl/certs/stock-analyse.crt"
SSL_KEY_PATH="/etc/ssl/private/stock-analyse.key"

# 管理员邮箱 - 用于Let's Encrypt证书申请 (如果启用SSL)
ADMIN_EMAIL="1187298955@qq.com"

# ====================================================
# 以下是高级配置，通常无需修改
# ====================================================

# 项目根目录
PROJECT_ROOT="/opt/stock-analyse"

# 后端发布目录
BACKEND_PUBLISH_DIR="$PROJECT_ROOT/publish/backend"

# 前端构建目录 (Vite outDir，默认为后端 wwwroot)
FRONTEND_DIST_DIR="$PROJECT_ROOT/src/StockAnalyse.Api/wwwroot"

# Python服务目录
PYTHON_SERVICE_DIR="$PROJECT_ROOT/python-data-service"

# 服务用户 (默认为当前用户)
SERVICE_USER="$USER"

# 是否启用防火墙规则
ENABLE_FIREWALL="true"

# 日志配置
LOG_PATH="/var/log/stock-analyse"
MAX_LOG_SIZE="10M"
LOG_RETENTION="30"
