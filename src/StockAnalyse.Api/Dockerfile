# ====================================================
# .NET 8 API Dockerfile - 多阶段构建
# ====================================================

# 阶段1: 构建阶段
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制解决方案和项目文件
COPY ["StockAnalyse.sln", "./"]
COPY ["src/StockAnalyse.Api/StockAnalyse.Api.csproj", "src/StockAnalyse.Api/"]

# 还原依赖
RUN dotnet restore "src/StockAnalyse.Api/StockAnalyse.Api.csproj"

# 复制所有源代码
COPY . .

# 构建项目
WORKDIR "/src/src/StockAnalyse.Api"
RUN dotnet build "StockAnalyse.Api.csproj" -c Release -o /app/build

# 阶段2: 发布阶段
FROM build AS publish
RUN dotnet publish "StockAnalyse.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 阶段3: 运行时阶段
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# 安装必要的工具和时区数据
RUN apt-get update && apt-get install -y \
    curl \
    tzdata \
    && rm -rf /var/lib/apt/lists/*

# 设置时区为中国时区
ENV TZ=Asia/Shanghai
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

# 创建非root用户
RUN useradd -m -u 1000 appuser && \
    mkdir -p /app/data /app/logs && \
    chown -R appuser:appuser /app

# 复制发布文件
COPY --from=publish /app/publish .

# 切换到非root用户
USER appuser

# 暴露端口
EXPOSE 5000

# 健康检查
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

# 启动应用
ENTRYPOINT ["dotnet", "StockAnalyse.Api.dll"]
