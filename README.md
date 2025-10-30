# 📈 股票分析系统

基于 .NET 8、Mapster、SQLite 开发的股票分析系统，提供实时行情、自选股管理、条件选股、技术分析、金融新闻、AI分析等功能。

## ✨ 功能特性

### 1. 自选股管理
- ✅ 添加/删除自选股
- ✅ 实时获取股价、最高价、最低价
- ✅ 设置成本价和持仓数量
- ✅ 自动计算盈亏
- ✅ 支持自定义分类（已购、预购等）
- ✅ 价格涨跌幅提醒

### 2. 条件选股
支持多种条件筛选：
- **基本面：** 股价、涨跌幅、换手率、排名、市盈率(PE)、市净率(PB)
- **技术面：** 金叉死叉
- **市值条件**

### 3. 日线图
- 获取历史K线数据
- 支持自定义时间范围
- 可接入图表库展示

### 4. 金融新闻
- 自动抓取财联社、新浪财经等新闻
- 按股票筛选新闻
- 关键词搜索
- **🆕 批量AI分析：** 对整个页面的新闻进行综合市场分析
- **🆕 智能分析：** 提供市场热点、行业板块、投资建议等多维度分析

### 5. AI分析
- 集成DeepSeek、OpenAI等大模型
- 股票分析建议
- 智能问答
- **🆕 新闻综合分析：** 基于多条新闻的市场趋势分析

### 6. 价格提醒
- 设置涨跌幅触发提醒
- 自动检查并触发通知
- 支持多种提醒类型

## 🚀 快速开始

### 环境要求
- .NET 8.0 SDK
- Visual Studio 2022 或 VS Code

### 安装步骤

1. **克隆项目**
```bash
git clone <your-repo-url>
cd StockAnalyse
```

2. **还原依赖**
```bash
dotnet restore
```

3. **运行项目**
```bash
cd src/StockAnalyse.Api
dotnet run
```

4. **访问系统**
- Web界面：http://localhost:5000 或 https://localhost:5001
- API文档：http://localhost:5000/swagger

## 📁 项目结构

```
StockAnalyse/
├── src/
│   └── StockAnalyse.Api/          # Web API项目
│       ├── Controllers/           # API控制器
│       ├── Services/              # 业务逻辑层
│       │   └── Interfaces/        # 服务接口
│       ├── Models/                # 数据模型
│       ├── Data/                  # 数据访问层
│       ├── wwwroot/               # 静态文件
│       └── Program.cs             # 主程序
├── StockAnalyse.sln              # 解决方案文件
└── README.md                     # 说明文档
```

## 🔧 配置

### SQLite数据库
系统首次运行会自动创建 `stockanalyse.db` 数据库文件。

### 股票数据源
默认使用新浪财经API获取实时行情：
```
http://hq.sinajs.cn/list={code}
```

### AI模型配置
在数据库中配置AI模型信息（API Key、Endpoint等）。

## 📡 API接口

### 股票相关
- `GET /api/stock/{code}` - 获取股票行情
- `POST /api/stock/batch` - 批量获取行情
- `GET /api/stock/{code}/history` - 获取日线数据
- `GET /api/stock/ranking/{market}` - 获取排名
- `POST /api/stock/{code}/macd` - 计算MACD

### 自选股相关
- `POST /api/watchlist/add` - 添加自选股
- `DELETE /api/watchlist/{id}` - 删除自选股
- `GET /api/watchlist/grouped` - 获取分组自选股
- `PUT /api/watchlist/{id}/cost` - 更新成本
- `GET /api/watchlist/categories` - 获取分类

### 条件选股
- `POST /api/screen/search` - 条件选股

### 新闻相关
- `GET /api/news/latest` - 最新新闻
- `GET /api/news/stock/{code}` - 股票新闻
- `GET /api/news/search` - 搜索新闻
- `POST /api/news/analyze-single` - 单条新闻AI分析
- `POST /api/news/analyze-batch` - 批量新闻AI分析
- `POST /api/news/analyze-latest` - 最新新闻综合分析
- `POST /api/news/analyze-single` - 单条新闻AI分析
- `POST /api/news/analyze-batch` - 批量新闻AI分析
- `POST /api/news/analyze-latest` - 最新新闻综合分析

### AI相关
- `POST /api/ai/analyze/{code}` - 分析股票
- `POST /api/ai/chat` - AI对话
- `GET /api/ai/recommend/{code}` - 获取建议

### 提醒相关
- `POST /api/alert/create` - 创建提醒
- `GET /api/alert/active` - 活跃提醒
- `DELETE /api/alert/{id}` - 删除提醒
- `POST /api/alert/check` - 检查提醒

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

- **后端：** .NET 8 Core
- **ORM：** Entity Framework Core
- **数据库：** SQLite
- **对象映射：** Mapster
- **API文档：** Swagger
- **日志：** Serilog

## 📝 注意事项

1. **股票代码格式：** 
   - 沪市：`600xxx`，在API中使用 `sh600xxx`
   - 深市：`000xxx`，在API中使用 `sz000xxx`

2. **数据来源：**
   - 股票行情数据来自新浪财经（仅供学习使用）
   - 实际生产环境建议使用官方或授权的数据接口

3. **定时任务：**
   - 价格提醒每60秒检查一次
   - 可根据需要调整检查频率

## 🔒 安全提示

- 请妥善保管AI模型的API Key
- 生产环境请配置HTTPS
- 建议添加用户认证和授权

## 📄 许可证

MIT License

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📧 联系方式

如有问题或建议，请提交 Issue。

---

Made with ❤️ using .NET 8


