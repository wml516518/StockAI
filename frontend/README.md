# 股票分析系统 - Vue 3 前端

基于 Vue 3 + Vite + Vue Router + Pinia 构建的现代化前端应用。

## 技术栈

- **Vue 3** - 渐进式 JavaScript 框架
- **Vite** - 下一代前端构建工具
- **Vue Router** - 官方路由管理器
- **Pinia** - 官方状态管理库
- **Axios** - HTTP 客户端
- **@vueuse/core** - Vue Composition API 工具集

## 项目结构

```
frontend/
├── src/
│   ├── components/     # 可复用组件
│   │   ├── Header.vue
│   │   └── Tabs.vue
│   ├── views/          # 页面组件
│   │   ├── Watchlist.vue
│   │   ├── Screen.vue
│   │   ├── QuantTrading.vue
│   │   ├── News.vue
│   │   ├── AI.vue
│   │   └── Settings.vue
│   ├── services/       # API 服务
│   │   ├── api.js
│   │   ├── stockService.js
│   │   └── watchlistService.js
│   ├── stores/         # Pinia 状态管理
│   │   └── watchlist.js
│   ├── router/         # 路由配置
│   │   └── index.js
│   ├── App.vue         # 根组件
│   ├── main.js         # 入口文件
│   └── style.css       # 全局样式
├── index.html
├── package.json
├── vite.config.js
└── README.md
```

## 快速开始

### 安装依赖

```bash
cd frontend
npm install
```

### 开发模式

```bash
npm run dev
```

前端开发服务器将运行在 `http://localhost:5173`，并自动代理 API 请求到后端（`http://localhost:5000`）。

### 构建生产版本

```bash
npm run build
```

构建产物将输出到 `../src/StockAnalyse.Api/wwwroot`，可以直接被后端服务使用。

## 功能模块

### 1. 自选股管理
- 添加/删除自选股
- 分类管理
- 实时价格刷新
- 盈亏计算

### 2. 条件选股
- 多维度筛选条件
- 实时查询结果

### 3. 量化交易
- 策略管理（开发中）
- 回测分析（开发中）

### 4. 金融新闻
- 新闻列表展示
- 关键词搜索

### 5. AI分析
- 股票AI分析

### 6. 价格提醒
- 创建价格提醒
- 提醒列表管理

### 7. 设置
- 自动刷新配置

## API 集成

所有 API 调用都通过 `src/services/api.js` 统一管理，支持：

- 请求/响应拦截器
- 错误处理
- 自动代理到后端服务器

## 状态管理

使用 Pinia 进行状态管理，当前已实现：

- `watchlist` store - 自选股相关状态和操作

## 开发说明

### 添加新页面

1. 在 `src/views/` 创建新的 Vue 组件
2. 在 `src/router/index.js` 添加路由配置
3. 在 `src/components/Tabs.vue` 添加标签页

### 添加新的 API 服务

1. 在 `src/services/` 创建服务文件
2. 使用 `api.js` 中的 axios 实例进行请求

### 添加新的状态管理

1. 在 `src/stores/` 创建新的 store 文件
2. 使用 `defineStore` 定义 store

## 注意事项

- 确保后端 API 服务正在运行（默认端口 5000）
- 开发时使用 Vite 的代理功能，生产环境需要配置正确的 API 地址
- 构建后的文件会覆盖 `wwwroot` 目录，注意备份

