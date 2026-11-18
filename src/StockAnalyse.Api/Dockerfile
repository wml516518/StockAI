# ====================================================
# .NET 10 API Dockerfile - 多阶段构建
# ====================================================

# 阶段1: 构建阶段
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
FROM ${DOTNET_SDK_IMAGE} AS build
ARG NUGET_SOURCE=https://api.nuget.org/v3/index.json
WORKDIR /src

# 复制解决方案和项目文件
COPY ["StockAnalyse.sln", "./"]
COPY ["src/StockAnalyse.Api/StockAnalyse.Api.csproj", "src/StockAnalyse.Api/"]

# 还原依赖
RUN dotnet restore "src/StockAnalyse.Api/StockAnalyse.Api.csproj" \
    --source "$NUGET_SOURCE"

# 复制所有源代码
COPY . .

# 构建项目
WORKDIR "/src/src/StockAnalyse.Api"
RUN dotnet build "StockAnalyse.Api.csproj" --no-restore -c Release -o /app/build

# 阶段2: 发布阶段
FROM build AS publish
RUN dotnet publish "StockAnalyse.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

# 阶段3: 运行时阶段
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG APT_MIRROR=
WORKDIR /app

RUN if [ -n "$APT_MIRROR" ]; then \
        sed -i \
            -e "s|http://ports.ubuntu.com/ubuntu-ports|https://${APT_MIRROR}/ubuntu-ports|g" \
            -e "s|http://archive.ubuntu.com/ubuntu|https://${APT_MIRROR}/ubuntu|g" \
            /etc/apt/sources.list.d/ubuntu.sources; \
    fi && \
    sed -i 's/^Components:.*/Components: main/' /etc/apt/sources.list.d/ubuntu.sources && \
    apt-get -o Acquire::http::Timeout=30 -o Acquire::https::Timeout=30 \
        -o Acquire::Retries=2 update && \
    apt-get -o Acquire::http::Timeout=30 -o Acquire::https::Timeout=30 \
        -o Acquire::Retries=2 install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

# 设置时区为中国时区
ENV TZ=Asia/Shanghai
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

# 创建非root用户
RUN mkdir -p /app/data /app/logs && \
    chown -R 1000:1000 /app/data /app/logs

# 复制发布文件
COPY --from=publish --chown=1000:1000 /app/publish .

# 切换到非root用户
USER 1000:1000

# 暴露端口
EXPOSE 5000

# 健康检查
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

# 启动应用
ENTRYPOINT ["dotnet", "StockAnalyse.Api.dll"]
