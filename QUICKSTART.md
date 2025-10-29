# 🚀 快速启动指南

## 1. 前置条件

确保已安装以下软件：
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 或 VS Code（可选）

## 2. 运行步骤

### 方法一：使用命令行

```bash
# 进入项目目录
cd src/StockAnalyse.Api

# 还原依赖
dotnet restore

# 运行项目
dotnet run
```

### 方法二：使用 Visual Studio

1. 打开 `StockAnalyse.sln`
2. 按 F5 或点击"运行"

## 3. 访问系统

启动成功后，访问：

- **Web界面：** http://localhost:5000
- **HTTPS界面：** https://localhost:5001
- **API文档：** http://localhost:5000/swagger

## 4. 使用说明

### 首次使用

1. 访问 http://localhost:5000
2. 系统会自动创建数据库和初始化数据

### 添加自选股

1. 切换到"自选股"标签
2. 输入股票代码（如：000001）
3. 选择分类（首次需创建分类）
4. 点击"添加到自选股"

### 股票代码格式

- 沪市：`600xxx`（如：600000）
- 深市：`000xxx`（如：000001）
- 创业板：`300xxx`
- 科创板：`688xxx`

注意：API中需要加上市场前缀
- 沪市：`sh600xxx`
- 深市：`sz000xxx`

### 使用条件选股

1. 切换到"条件选股"标签
2. 设置筛选条件（价格、涨跌幅、市盈率等）
3. 点击"开始选股"
4. 查看字结果

### 查看新闻

1. 切换到"金融新闻"标签
2. 系统会自动加载最新新闻

### 使用AI分析

1. 切换到"AI分析"标签
2. 输入股票代码
3. 点击"开始分析"

注意：首次使用需要配置AI模型（DeepSeek或OpenAI）

### 设置价格提醒

1. 切换到"价格提醒"标签
2. 输入股票代码和目标价格
3. 选择提醒类型
4！（点击"创建提醒"
5. 系统会每分钟自动检查并触发提醒

## 5. 配置AI模型（可选）

如果要使用AI分析功能，需要配置AI API：

1. 在数据库中插入配置记录（可通过SQL或API）
2. 配置字段：
   - `Name`: 模型名称（如：DeepSeek）
   - `ApiKey`: API密钥
   - `SubscribeEndpoint`: API端点
   - `ModelName`: 模型名称
   - `IsActive`: true
   - `IsDefault`: true

示例SQL：
```sql
INSERT INTO AIModelConfigs (Name, ApiKey, SubscribeEndpoint, ModelName, IsActive, IsDefault)
VALUES ('DeepSeek', 'your-api-key', 'https://api.deepseek.com/v1/chat/completions', 'deepseek-chat', 1, 1);
```

## 6. 常见问题

### Q: 无法获取股票数据？
A: 检查网络连接，确保可以访问新浪财经API。如果是内网环境，可能需要配置代理。

### Q: 数据库文件在哪里？
A: 数据库文件 `stockanalyse.db` 会在项目运行目录下自动创建。

### Q: 价格提醒不工作？
A: 确保系统正在运行，价格提醒每分钟检查一次。

### Q: AI分析失败？
A: 确保已正确配置AI模型的API Key和Endpoint。

### Q: 如何添加更多股票分类？
A: 在"自选股"页面会显示分类选项，首次使用时可以手动添加分类。

## 7. 下一步

- 查看完整API文档：访问 `/swagger`
- 定制化开发：查看源代码了解业务逻辑
- 扩展功能：添加更多技术指标和数据分析

## 8. 技术支持

遇到问题？请：
1. 检查日志输出
2. 查看本文档
3. 提交Issue到项目仓库

---

祝使用愉快！📈


