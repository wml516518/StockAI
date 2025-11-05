# 📈 股票分析系统

基于 .NET 8、Entity Framework Core、SQLite 开发的股票分析系统，提供实时行情、自选股管理、条件选股、量化交易、技术分析、金融新闻、AI分析等完整功能。

## ✨ 核心功能模块

### 1. 自选股管理 📋
**功能概述：** 完整的自选股管理系统，支持分类、成本跟踪、盈亏计算等功能。

**核心特性：**
- ✅ 添加/删除自选股
- ✅ 实时获取股价、最高价、最低价、涨跌幅等行情数据
- ✅ 支持自定义分类（已购、预购、关注等），支持颜色标记
- ✅ 设置成本价和持仓数量，自动计算总成本
- ✅ 实时计算盈亏金额和盈亏百分比
- ✅ 价格涨跌幅提醒（上涨/下跌/到达目标价）
- ✅ 自动刷新行情（可配置刷新间隔）
- ✅ 按分类分组显示，便于管理

**数据模型：**
- `WatchlistStock` - 自选股记录
- `WatchlistCategory` - 自选股分类

### 2. 条件选股 🔍
**功能概述：** 多维度股票筛选工具，支持保存/加载筛选模板。

**筛选条件：**
- **基本面指标：**
  - 价格区间（最低价/最高价）
  - 涨跌幅区间（%）
  - 换手率区间（%）
  - 成交量区间（手）
  - 市值区间（万元）
  - 市盈率(PE)区间
  - 市净率(PB)区间
  - 股息率区间（%）
- **市场筛选：** 上海市场/深圳市场/全部市场
- **技术指标：** MACD金叉死叉（可通过技术指标服务扩展）

**模板管理：**
- 保存筛选条件为模板
- 加载已保存的模板
- 编辑/删除模板
- 设置默认模板

**数据模型：**
- `ScreenTemplate` - 选股条件模板
- `ScreenCriteria` - 选股条件

### 3. 量化交易 📊
**功能概述：** 完整的量化交易系统，包括策略管理、回测分析、实时监控等功能。

#### 3.1 策略管理
- **策略类型：**
  - 技术指标策略（MA、MACD、RSI等）
  - 基本面策略
  - 套利策略
  - 机器学习策略
  - 自定义策略
- **策略操作：**
  - 创建/编辑/删除策略
  - 策略参数配置（JSON格式存储）
  - 导入默认策略模板
  - 启用/禁用策略
  - 初始资金设置

#### 3.2 回测分析
- **一键回测：** 使用简单移动平均策略快速体验
- **批量回测：** 支持多股票批量回测
- **回测指标：**
  - 总收益率
  - 年化收益率
  - 最大回撤
  - 夏普比率
  - 交易次数
  - 胜率
- **交易记录：** 详细的买卖记录查看
- **回测结果保存：** 历史回测结果可查询

#### 3.3 实时监控
- 实时监控活跃策略
- 自动生成交易信号
- 策略执行状态追踪

#### 3.4 策略优化
- 参数优化（网格搜索）
- 多参数组合测试
- 优化结果对比分析
- 应用优化参数到策略

**数据模型：**
- `QuantStrategy` - 量化策略
- `TradingSignal` - 交易信号
- `SimulatedTrade` - 模拟交易记录
- `BacktestResult` - 回测结果
- `StrategyOptimizationResult` - 策略优化结果
- `ParameterTestResult` - 参数测试结果

### 4. 技术指标 📈
**功能概述：** 技术指标计算服务，支持多种技术指标。

**支持的指标：**
- **移动平均线（MA）：** 简单移动平均、指数移动平均
- **MACD：** 平滑异同移动平均线
- **RSI：** 相对强弱指标
- **布林带（Bollinger Bands）**
- **KDJ指标**
- **更多指标可扩展**

**服务接口：**
- `ITechnicalIndicatorService` - 技术指标计算服务

### 5. 金融新闻 📰
**功能概述：** 金融新闻抓取、搜索和AI分析功能。

**核心特性：**
- **新闻抓取：**
  - 自动抓取财联社、新浪财经等新闻源
  - 定时自动刷新（可配置间隔）
  - 支持立即手动刷新
- **新闻管理：**
  - 查看最新新闻列表
  - 按股票代码筛选相关新闻
  - 关键词搜索新闻
  - 新闻详情查看
- **AI分析：**
  - 单条新闻AI分析
  - 批量新闻综合分析
  - 最新新闻市场分析
  - 选择不同的AI提示词进行分析

**后台服务：**
- `NewsBackgroundService` - 定时抓取新闻的后台服务

**数据模型：**
- `FinancialNews` - 金融新闻

### 6. AI分析 🤖
**功能概述：** 集成大语言模型的AI分析功能。

**核心特性：**
- **多模型支持：**
  - DeepSeek API
  - OpenAI API
  - 支持自定义模型配置
- **提示词管理：**
  - 创建/编辑/删除提示词
  - 设置默认提示词
  - 提示词温度参数配置
  - 提示词启用/禁用
- **分析功能：**
  - 股票基本面分析
  - 股票技术面分析
  - 新闻综合分析
  - AI对话问答
- **配置管理：**
  - AI模型配置（API Key、Endpoint、Model Name）
  - 激活配置切换
  - 默认配置设置
  - 连接测试

**数据模型：**
- `AIModelConfig` - AI模型配置
- `AIPrompt` - AI提示词配置

### 7. 价格提醒 🔔
**功能概述：** 股票价格到达目标价时的自动提醒功能。

**核心特性：**
- 创建价格提醒（支持上涨/下跌/到达目标价）
- 支持价格提醒和百分比提醒
- 自动检查并触发提醒（每分钟检查一次）
- 查看活跃提醒列表
- 删除提醒
- 手动触发检查

**数据模型：**
- `PriceAlert` - 价格提醒

### 8. 股票数据 📊
**功能概述：** 股票行情数据获取和历史数据管理。

**核心特性：**
- **实时行情：**
  - 获取单只股票行情
  - 批量获取多只股票行情
  - 获取市场排名数据
- **历史数据：**
  - 获取日线K线数据
  - 支持自定义时间范围
- **技术指标计算：**
  - MACD计算
  - 其他技术指标计算

**数据模型：**
- `Stock` - 股票基础信息
- `StockHistory` - 股票历史数据

## 🚀 快速开始

### 环境要求
- .NET 8.0 SDK
- Node.js 18+ (前端开发需要)
- Python 3.8+ (可选，用于Python数据服务)
- Visual Studio 2022 或 VS Code

### 安装步骤

1. **克隆项目**
```bash
git clone <your-repo-url>
cd StockAnalyse
```

2. **还原后端依赖**
```bash
cd src/StockAnalyse.Api
dotnet restore
cd ../..
```

3. **安装前端依赖（可选）**
```bash
cd frontend
npm install
cd ..
```

4. **启动服务**

**方式1：一键启动所有服务（推荐）**
```bash
# Windows
start-all-services.bat

# PowerShell
.\start-all-services.ps1
```

**方式2：分别启动各个服务**
```bash
# 启动后端API服务
start-backend.bat
# 或
cd src/StockAnalyse.Api
dotnet run

# 启动前端开发服务器（新终端）
start-frontend-only.bat
# 或
cd frontend
npm run dev

# 启动Python数据服务（可选，新终端）
cd python-data-service
python stock_data_service.py
```

5. **访问系统**
- 前端界面：http://localhost:5173
- 后端API：http://localhost:5000
- API文档：http://localhost:5000/swagger
- Python数据服务：http://localhost:5001（如果已启动）

### 脚本说明

项目提供了多个便捷脚本：

- `start-all-services.bat/ps1` - 一键启动所有服务（后端、前端、Python服务）
- `start-backend.bat` - 仅启动后端API服务
- `start-frontend-only.bat` - 仅启动前端开发服务器
- `stop-all-services.bat/ps1` - 停止所有服务
- `check-services.bat` - 检查各服务运行状态

## 📁 项目结构

```
StockAnalyse/
├── src/
│   └── StockAnalyse.Api/              # Web API项目
│       ├── Controllers/               # API控制器（15个控制器）
│       │   ├── AIController.cs              # AI分析控制器
│       │   ├── AIModelConfigController.cs    # AI模型配置控制器
│       │   ├── AIPromptController.cs         # AI提示词控制器
│       │   ├── AIPromptsController.cs        # AI提示词控制器（备用）
│       │   ├── AlertController.cs           # 价格提醒控制器
│       │   ├── BacktestController.cs         # 回测控制器
│       │   ├── NewsController.cs             # 金融新闻控制器
│       │   ├── QuantTradingController.cs     # 量化交易控制器
│       │   ├── ScreenController.cs          # 条件选股控制器
│       │   ├── ScreenTemplateController.cs   # 选股模板控制器
│       │   ├── SimpleBacktestController.cs   # 简单回测控制器
│       │   ├── StockController.cs            # 股票数据控制器
│       │   ├── StrategyConfigController.cs   # 策略配置控制器
│       │   ├── StrategyOptimizationController.cs # 策略优化控制器
│       │   └── WatchlistController.cs        # 自选股控制器
│       ├── Services/                  # 业务逻辑层
│       │   ├── Interfaces/            # 服务接口定义
│       │   │   ├── IAIService.cs
│       │   │   ├── IBacktestService.cs
│       │   │   ├── INewsService.cs
│       │   │   ├── IPriceAlertService.cs
│       │   │   ├── IQuantTradingService.cs
│       │   │   ├── IScreenService.cs
│       │   │   ├── IStockDataService.cs
│       │   │   ├── IStrategyConfigService.cs
│       │   │   ├── IStrategyOptimizationService.cs
│       │   │   ├── ITechnicalIndicatorService.cs
│       │   │   └── IWatchlistService.cs
│       │   ├── AIPromptConfigService.cs      # AI提示词配置服务
│       │   ├── AIService.cs                 # AI分析服务
│       │   ├── BacktestService.cs            # 回测服务
│       │   ├── NewsService.cs                # 新闻服务
│       │   ├── NewsBackgroundService.cs      # 新闻后台抓取服务
│       │   ├── PriceAlertService.cs          # 价格提醒服务
│       │   ├── QuantTradingService.cs        # 量化交易服务
│       │   ├── ScreenService.cs              # 条件选股服务
│       │   ├── StockDataService.cs           # 股票数据服务
│       │   ├── StrategyConfigService.cs      # 策略配置服务
│       │   ├── StrategyOptimizationService.cs # 策略优化服务
│       │   ├── TechnicalIndicatorService.cs   # 技术指标服务
│       │   └── WatchlistService.cs           # 自选股服务
│       ├── Models/                   # 数据模型
│       │   └── Stock.cs              # 所有数据模型定义
│       ├── Data/                     # 数据访问层
│       │   ├── StockDbContext.cs     # EF Core数据库上下文
│       │   └── DatabaseSeeder.cs     # 数据库初始化种子数据
│       ├── Migrations/               # EF Core迁移文件
│       ├── wwwroot/                  # 静态文件（前端）
│       │   ├── index.html            # 前端主页面
│       │   ├── script.js             # 前端JavaScript
│       │   └── style.css             # 前端样式
│       ├── strategy-configs/         # 策略配置JSON文件
│       │   ├── ma-cross-strategy.json
│       │   ├── macd-strategy.json
│       │   ├── rsi-strategy.json
│       │   ├── simple-ma-strategy.json
│       │   └── 简单移动平均策略.json
│       ├── stockanalyse.db           # SQLite数据库文件（自动生成）
│       ├── Program.cs                # 主程序入口
│       └── StockAnalyse.Api.csproj   # 项目文件
├── StockAnalyse.sln                 # 解决方案文件
├── README.md                        # 项目说明文档
└── QUICKSTART.md                    # 快速启动指南
```

## 🔧 系统配置

### 数据库配置
- **数据库类型：** SQLite
- **数据库文件：** `stockanalyse.db`（自动创建在项目运行目录）
- **迁移：** 系统首次运行会自动执行数据库迁移，创建表结构
- **初始化：** 首次运行会自动执行种子数据初始化

### 股票数据源配置
- **默认数据源：** 新浪财经API
- **API格式：** `http://hq.sinajs.cn/list={code}`
- **代码格式：**
  - 沪市：`sh600xxx`（如：sh600000）
  - 深市：`sz000xxx`（如：sz000001）
  - 创业板：`sz300xxx`
  - 科创板：`sh688xxx`

**注意：** 数据源仅供学习使用，生产环境请使用官方或授权数据接口。

### AI模型配置
系统支持配置多个AI模型，通过数据库或Web界面配置：

**配置项：**
- `Name` - 模型名称（如：DeepSeek、OpenAI）
- `ApiKey` - API密钥
- `SubscribeEndpoint` - API端点URL
- `ModelName` - 模型名称（如：qwen-max、gpt-4）
- `IsActive` - 是否激活（激活后系统使用此配置）
- `IsDefault` - 是否默认配置

**配置方式：**
1. 通过Web界面：设置 → AI模型配置
2. 通过API：`/api/aimodelconfig`
3. 通过数据库：直接操作 `AIModelConfigs` 表

### 自动刷新配置
- **自选股刷新间隔：** 默认3秒，可在设置中配置（0.5-60秒）
- **新闻刷新间隔：** 默认30分钟，可在设置中配置（5-1440分钟）
- **价格提醒检查：** 每分钟自动检查一次

### 策略配置
系统内置了多个策略模板（JSON格式），位于 `strategy-configs/` 目录：
- `simple-ma-strategy.json` - 简单移动平均策略
- `ma-cross-strategy.json` - 均线交叉策略
- `macd-strategy.json` - MACD策略
- `rsi-strategy.json` - RSI策略

可以导入这些模板或创建自定义策略。

## 📡 API接口文档

### 股票数据接口 (`/api/stock`)
- `GET /api/stock/{code}` - 获取单只股票行情
- `POST /api/stock/batch` - 批量获取多只股票行情
- `GET /api/stock/{code}/history` - 获取股票历史日线数据
- `GET /api/stock/ranking/{market}` - 获取市场排名数据
- `POST /api/stock/{code}/macd` - 计算股票MACD指标

### 自选股接口 (`/api/watchlist`)
- `POST /api/watchlist/add` - 添加自选股
- `DELETE /api/watchlist/{id}` - 删除自选股
- `GET /api/watchlist/grouped` - 获取按分类分组的自选股列表
- `PUT /api/watchlist/{id}/cost` - 更新自选股成本价和持仓数量
- `GET /api/watchlist/categories` - 获取所有自选股分类
- `POST /api/watchlist/categories` - 创建新分类
- `PUT /api/watchlist/categories/{id}` - 更新分类
- `DELETE /api/watchlist/categories/{id}` - 删除分类

### 条件选股接口 (`/api/screen`)
- `POST /api/screen/search` - 执行条件选股查询

### 选股模板接口 (`/api/screentemplate`)
- `GET /api/screentemplate` - 获取所有选股模板
- `GET /api/screentemplate/{id}` - 获取指定模板
- `POST /api/screentemplate` - 创建新模板
- `PUT /api/screentemplate/{id}` - 更新模板
- `DELETE /api/screentemplate/{id}` - 删除模板

### 金融新闻接口 (`/api/news`)
- `GET /api/news/latest` - 获取最新新闻列表
- `GET /api/news/stock/{code}` - 获取指定股票的相关新闻
- `GET /api/news/search` - 搜索新闻（关键词）
- `POST /api/news/analyze-single` - 单条新闻AI分析
- `POST /api/news/analyze-batch` - 批量新闻AI综合分析
- `POST /api/news/analyze-latest` - 最新新闻市场分析
- `POST /api/news/fetch` - 手动触发新闻抓取

### AI分析接口 (`/api/ai`)
- `POST /api/ai/analyze/{code}` - 分析指定股票（使用AI）
- `POST /api/ai/chat` - AI对话问答
- `GET /api/ai/recommend/{code}` - 获取股票投资建议

### AI模型配置接口 (`/api/aimodelconfig`)
- `GET /api/aimodelconfig` - 获取所有AI模型配置
- `GET /api/aimodelconfig/{id}` - 获取指定配置
- `POST /api/aimodelconfig` - 创建新配置
- `PUT /api/aimodelconfig/{id}` - 更新配置
- `DELETE /api/aimodelconfig/{id}` - 删除配置
- `POST /api/aimodelconfig/{id}/test` - 测试配置连接
- `PUT /api/aimodelconfig/{id}/activate` - 激活配置

### AI提示词接口 (`/api/aiprompt`)
- `GET /api/aiprompt` - 获取所有提示词
- `GET /api/aiprompt/{id}` - 获取指定提示词
- `POST /api/aiprompt` - 创建新提示词
- `PUT /api/aiprompt/{id}` - 更新提示词
- `DELETE /api/aiprompt/{id}` - 删除提示词

### 价格提醒接口 (`/api/alert`)
- `POST /api/alert/create` - 创建价格提醒
- `GET /api/alert/active` - 获取所有活跃提醒
- `DELETE /api/alert/{id}` - 删除提醒
- `POST /api/alert/check` - 手动触发提醒检查

### 量化交易接口 (`/api/quanttrading`)
- `GET /api/quanttrading/strategies` - 获取所有策略
- `GET /api/quanttrading/strategies/{id}` - 获取指定策略
- `POST /api/quanttrading/strategies` - 创建新策略
- `PUT /api/quanttrading/strategies/{id}` - 更新策略
- `DELETE /api/quanttrading/strategies/{id}` - 删除策略
- `POST /api/quanttrading/strategies/import-default` - 导入默认策略
- `POST /api/quanttrading/strategies/{id}/activate` - 激活策略
- `GET /api/quanttrading/strategies/{id}/signals` - 获取策略交易信号
- `POST /api/quanttrading/monitoring/start` - 开始实时监控
- `POST /api/quanttrading/monitoring/stop` - 停止实时监控
- `GET /api/quanttrading/monitoring/status` - 获取监控状态
- `GET /api/quanttrading/active-strategies` - 获取活跃策略列表

### 回测接口 (`/api/backtest`)
- `POST /api/backtest/run-batch` - 执行批量回测
- `GET /api/backtest/results/{id}` - 获取回测结果详情
- `GET /api/backtest/results` - 获取所有回测结果

### 简单回测接口 (`/api/simplebacktest`)
- `POST /api/simplebacktest` - 执行简单回测（一键回测）

### 策略配置接口 (`/api/strategyconfig`)
- `GET /api/strategyconfig` - 获取所有策略配置
- `GET /api/strategyconfig/{id}` - 获取指定策略配置
- `POST /api/strategyconfig` - 创建策略配置
- `PUT /api/strategyconfig/{id}` - 更新策略配置
- `DELETE /api/strategyconfig/{id}` - 删除策略配置

### 策略优化接口 (`/api/strategyoptimization`)
- `POST /api/strategyoptimization/optimize` - 执行策略优化
- `GET /api/strategyoptimization/results/{id}` - 获取优化结果
- `GET /api/strategyoptimization/results` - 获取所有优化结果
- `POST /api/strategyoptimization/results/{id}/apply` - 应用优化结果到策略

## 💡 使用示例

### 添加自选股
```javascript
POST /api/watchlist/add
{
  "stockCode": "000001",
  "categoryId": 1,
  "costPrice": 12.50,
  "quantity": 1000
}
```

### 条件选股
```javascript
POST /api/screen/search
{
  "minPrice": 10,
  "maxPrice": 50,
  "minChangePercent": 2,
  "maxChangePercent": 10,
  "minTurnoverRate": 3,
  "minPE": 10,
  "maxPE": 30,
  "MACD混乱Up": true
}
```

### 批量新闻AI分析
```javascript
POST /api/news/analyze-batch
{
  "newsIds": [1, 2, 3, 4, 5]
}
```

### 最新新闻综合分析
```javascript
POST /api/news/analyze-latest
{
  "count": 30,
  "hours": 24
}
```

## 🛠️ 技术栈

### 后端技术
- **框架：** .NET 8.0
- **Web框架：** ASP.NET Core Web API
- **ORM：** Entity Framework Core 8.0
- **数据库：** SQLite
- **API文档：** Swagger/OpenAPI
- **CORS：** 支持跨域请求
- **静态文件：** 支持静态文件服务

### 前端技术
- **HTML/CSS/JavaScript：** 原生前端（无框架依赖）
- **UI：** 响应式设计，支持移动端

### 数据存储
- **数据库：** SQLite（文件数据库）
- **数据模型：** Entity Framework Core Code First

### 第三方服务集成
- **股票数据：** 新浪财经API
- **AI服务：** DeepSeek API、OpenAI API（可扩展）

### 后台服务
- **定时任务：** `IHostedService` 实现新闻自动抓取
- **定时检查：** `System.Timers.Timer` 实现价格提醒检查

## 📝 开发指南

### 数据模型说明
系统使用 Entity Framework Core Code First 方式管理数据库，所有数据模型定义在 `Models/Stock.cs` 文件中：

**核心数据模型：**
- `Stock` - 股票基础信息和实时行情
- `StockHistory` - 股票历史K线数据
- `WatchlistStock` - 自选股记录
- `WatchlistCategory` - 自选股分类
- `FinancialNews` - 金融新闻
- `PriceAlert` - 价格提醒
- `QuantStrategy` - 量化策略
- `TradingSignal` - 交易信号
- `SimulatedTrade` - 模拟交易记录
- `BacktestResult` - 回测结果
- `ScreenTemplate` - 选股条件模板
- `AIModelConfig` - AI模型配置
- `AIPrompt` - AI提示词配置
- `StrategyOptimizationResult` - 策略优化结果

### 服务接口说明
系统采用服务接口模式，所有业务逻辑封装在服务层：

**主要服务接口：**
- `IStockDataService` - 股票数据服务
- `IWatchlistService` - 自选股服务
- `IScreenService` - 条件选股服务
- `INewsService` - 新闻服务
- `IAIService` - AI分析服务
- `IPriceAlertService` - 价格提醒服务
- `IQuantTradingService` - 量化交易服务
- `IBacktestService` - 回测服务
- `ITechnicalIndicatorService` - 技术指标服务
- `IStrategyConfigService` - 策略配置服务
- `IStrategyOptimizationService` - 策略优化服务

### 添加新功能
1. **添加新的数据模型：** 在 `Models/Stock.cs` 中定义
2. **创建迁移：** `dotnet ef migrations add MigrationName`
3. **应用迁移：** `dotnet ef database update`
4. **创建服务接口：** 在 `Services/Interfaces/` 中定义接口
5. **实现服务：** 在 `Services/` 中实现服务类
6. **创建控制器：** 在 `Controllers/` 中创建API控制器
7. **注册服务：** 在 `Program.cs` 中注册服务依赖

### 扩展技术指标
1. 在 `ITechnicalIndicatorService` 接口中添加新方法
2. 在 `TechnicalIndicatorService` 中实现计算方法
3. 在相关控制器中暴露API接口

### 添加新的策略类型
1. 在 `StrategyType` 枚举中添加新类型
2. 在策略参数模型中添加对应参数
3. 在策略执行服务中实现策略逻辑
4. 创建策略配置JSON模板

## 📝 注意事项

1. **股票代码格式：** 
   - 输入时使用：`000001`、`600000` 等
   - API内部使用：`sz000001`、`sh600000`（系统会自动转换）

2. **数据来源：**
   - 股票行情数据来自新浪财经、东方财富、腾讯财经等（仅供学习使用）
   - 实际生产环境建议使用官方或授权的数据接口
   - 新闻数据来自财联社、新浪财经等
   - Python服务使用AKShare数据源（可选，提供更完整的财务数据）

3. **定时任务：**
   - 价格提醒每60秒检查一次（可在 `Program.cs` 中调整）
   - 新闻自动刷新间隔可配置（默认30分钟）
   - 自选股刷新间隔可配置（默认3秒）

4. **数据库迁移：**
   - 首次运行会自动创建数据库和表
   - 添加新模型后需要创建迁移：`dotnet ef migrations add MigrationName`
   - 应用迁移：`dotnet ef database update`

5. **AI配置：**
   - 使用AI功能前必须配置至少一个AI模型
   - API Key需要妥善保管，不要提交到代码仓库
   - 建议使用环境变量或配置文件存储敏感信息

6. **日志系统：**
   - 系统使用结构化日志（ILogger），支持不同日志级别
   - 开发环境：详细日志输出
   - 生产环境：仅输出警告和错误日志
   - 所有调试日志已统一使用 `_logger.LogDebug()` 而非 `Console.WriteLine()`

## 🔒 安全提示

1. **API密钥安全：**
   - 请妥善保管AI模型的API Key
   - 不要将API Key提交到代码仓库
   - 建议使用环境变量或密钥管理服务存储敏感信息

2. **生产环境配置：**
   - 必须配置HTTPS
   - 建议添加用户认证和授权（JWT、OAuth等）
   - 配置CORS白名单，限制跨域请求
   - 启用请求限流和防DDoS攻击

3. **数据库安全：**
   - SQLite数据库文件应设置适当权限
   - 定期备份数据库文件
   - 敏感数据应加密存储

4. **代码安全：**
   - 避免SQL注入：使用参数化查询
   - 避免XSS攻击：对用户输入进行验证和转义
   - 定期更新依赖包，修复安全漏洞

## 📊 功能模块总结表

| 模块 | 控制器 | 服务接口 | 数据模型 | 主要功能 |
|------|--------|----------|----------|----------|
| 自选股管理 | WatchlistController | IWatchlistService | WatchlistStock, WatchlistCategory | 自选股增删改查、分类管理、盈亏计算 |
| 条件选股 | ScreenController | IScreenService | ScreenTemplate, ScreenCriteria | 多条件筛选、模板管理 |
| 量化交易 | QuantTradingController | IQuantTradingService | QuantStrategy, TradingSignal, SimulatedTrade | 策略管理、实时监控 |
| 回测分析 | BacktestController, SimpleBacktestController | IBacktestService | BacktestResult | 策略回测、性能分析 |
| 策略优化 | StrategyOptimizationController | IStrategyOptimizationService | StrategyOptimizationResult | 参数优化、性能提升 |
| 策略配置 | StrategyConfigController | IStrategyConfigService | QuantStrategy | 策略配置管理 |
| 技术指标 | StockController | ITechnicalIndicatorService | - | MACD、RSI等指标计算 |
| 金融新闻 | NewsController | INewsService | FinancialNews | 新闻抓取、搜索、AI分析 |
| AI分析 | AIController | IAIService | AIModelConfig, AIPrompt | 股票分析、对话问答 |
| AI配置 | AIModelConfigController | - | AIModelConfig | AI模型配置管理 |
| AI提示词 | AIPromptController | - | AIPrompt | 提示词管理 |
| 价格提醒 | AlertController | IPriceAlertService | PriceAlert | 价格监控、自动提醒 |
| 股票数据 | StockController | IStockDataService | Stock, StockHistory | 行情获取、历史数据 |

## 🚧 后续开发建议

### 功能扩展方向

1. **用户系统**
   - 用户注册/登录
   - 多用户数据隔离
   - 用户权限管理
   - 个人设置保存

2. **数据可视化**
   - K线图展示（集成图表库，如ECharts）
   - 技术指标可视化
   - 回测结果图表化
   - 数据分析报表

3. **通知系统**
   - 邮件通知
   - 短信通知
   - 微信通知（集成企业微信）
   - 浏览器推送通知

4. **高级技术指标**
   - 更多技术指标支持（KDJ、CCI、BOLL等）
   - 自定义指标计算
   - 指标组合分析

5. **策略回测增强**
   - 可视化回测曲线
   - 回测参数对比
   - 回测报告生成
   - 多策略组合回测

6. **数据源扩展**
   - 支持更多股票数据源
   - 实时数据推送（WebSocket）
   - 数据缓存机制
   - 数据质量监控

7. **性能优化**
   - 数据缓存（Redis）
   - 异步任务处理
   - 批量操作优化
   - 数据库索引优化

8. **移动端支持**
   - 响应式设计优化
   - PWA支持
   - 移动端APP（可选）

9. **数据导出**
   - Excel导出
   - PDF报告生成
   - 数据备份/恢复

10. **社交功能**
    - 策略分享
    - 社区讨论
    - 排行榜

### 代码质量提升

1. **单元测试**
   - 为服务层添加单元测试
   - 为控制器添加集成测试
   - 测试覆盖率目标：80%+

2. **代码规范**
   - 代码注释完善
   - API文档完善
   - 代码审查流程

3. **日志和监控**
   - 结构化日志
   - 性能监控
   - 错误追踪（如集成Sentry）

4. **CI/CD**
   - 自动化构建
   - 自动化测试
   - 自动化部署

## 📚 相关文档

- [QUICKSTART.md](./QUICKSTART.md) - 快速启动指南
- [API文档](http://localhost:5000/swagger) - Swagger API文档（运行后访问）

## 📄 许可证

MIT License

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

**贡献指南：**
1. Fork 本项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 📧 联系方式

如有问题或建议，请提交 Issue。

## 🙏 致谢

感谢以下开源项目和服务：
- .NET 8
- Entity Framework Core
- SQLite
- Swagger/OpenAPI
- 新浪财经（数据源）

---

Made with ❤️ using .NET 8


