# ====================================================
# 前端 Dockerfile - 多阶段构建
# ====================================================

# 阶段1: 构建阶段
FROM node:22-alpine AS build
ARG VITE_BASE_PATH=/
ENV VITE_BASE_PATH=$VITE_BASE_PATH

WORKDIR /app

# 复制package.json和package-lock.json
COPY package*.json ./

# 安装依赖（使用国内镜像加速）
RUN npm config set registry https://registry.npmmirror.com && \
    npm ci

# 复制源代码
COPY . .

# 构建前端 (覆盖输出目录为dist)
RUN npm run build -- --outDir dist

# 阶段2: 运行时阶段
FROM nginx:alpine

# 安装健康检查工具
RUN apk add --no-cache curl

# 复制Nginx配置
COPY nginx.conf /etc/nginx/nginx.conf

# 复制构建产物
COPY --from=build /app/dist /usr/share/nginx/html

# 创建日志目录
RUN mkdir -p /var/log/nginx && \
    chown -R nginx:nginx /var/log/nginx /usr/share/nginx/html

# 暴露端口
EXPOSE 80

# 健康检查
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

# 登录由后端会话 API 处理
ENTRYPOINT ["nginx", "-g", "daemon off;"]
