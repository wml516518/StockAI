# Vue 3 前端快速启动指南

## 前置要求

- Node.js 16+ 和 npm
- 后端 API 服务正在运行（默认端口 5000）

## 安装和启动

### 1. 安装依赖

```bash
cd frontend
npm install
```

### 2. 启动开发服务器

```bash
npm run dev
```

前端应用将在 `http://localhost:5173` 启动，并自动代理 API 请求到后端。

### 3. 构建生产版本

```bash
npm run build
```

构建产物将输出到 `../src/StockAnalyse.Api/wwwroot`，可以直接被后端服务使用。

## 开发工作流

### 开发模式（推荐）

1. 启动后端 API 服务：
   ```bash
   cd src/StockAnalyse.Api
   dotnet run
   ```

2. 在另一个终端启动前端开发服务器：
   ```bash
   cd frontend
   npm run dev
   ```

3. 访问 `http://localhost:5173` 查看前端应用

### 生产模式

1. 构建前端：
   ```bash
   cd frontend
   npm run build
   ```

2. 启动后端服务（会自动提供前端静态文件）：
   ```bash
   cd src/StockAnalyse.Api
   dotnet run
   ```

3. 访问 `http://localhost:5000` 查看完整应用

## 项目结构说明

```
frontend/
├── src/
│   ├── components/      # 可复用组件
│   ├── views/          # 页面组件
│   ├── services/       # API 服务
│   ├── stores/         # Pinia 状态管理
│   ├── router/         # 路由配置
│   └── main.js         # 入口文件
├── package.json        # 依赖配置
├── vite.config.js      # Vite 配置
└── index.html         # HTML 模板
```

## 主要功能

✅ **自选股管理** - 完整的自选股功能，支持分类、实时刷新、盈亏计算
✅ **条件选股** - 多维度股票筛选
✅ **价格提醒** - 创建和管理价格提醒
✅ **金融新闻** - 新闻列表和搜索
✅ **AI分析** - 股票AI分析
✅ **设置** - 自动刷新配置

## 常见问题

### 端口冲突

如果 5173 端口被占用，Vite 会自动选择其他端口。或者修改 `vite.config.js` 中的 `server.port`。

### API 请求失败

确保后端 API 服务正在运行，并且端口配置正确（默认 5000）。

### 构建后无法访问

检查构建输出目录是否正确（`../src/StockAnalyse.Api/wwwroot`），确保后端服务配置了静态文件支持。

## 技术特性

- ⚡ **快速开发** - Vite 提供极速的 HMR（热模块替换）
- 🎯 **TypeScript 支持** - 可以轻松添加 TypeScript（可选）
- 📦 **代码分割** - 自动代码分割，优化加载性能
- 🔄 **状态管理** - 使用 Pinia 进行响应式状态管理
- 🎨 **现代化 UI** - 响应式设计，支持移动端

