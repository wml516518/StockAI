# AKShare与AI功能集成说明

## ✅ 集成状态

AKShare Python服务已成功集成到AI分析功能中！

## 🔗 集成架构

```
AI分析请求
    ↓
AIController.AnalyzeStock()
    ↓
StockDataService.GetFundamentalInfoAsync()
    ↓
TryGetFundamentalInfoFromPythonServiceAsync()  ← 优先使用
    ↓
Python服务 (http://localhost:5001)
    ↓
AKShare库获取基本面数据
    ↓
返回给AI进行分析
```

## 📊 数据流程

1. **AI分析请求** → `POST /api/ai/analyze/{stockCode}`
2. **获取基本面数据** → `StockDataService.GetFundamentalInfoAsync()`
   - 优先调用Python服务（AKShare）
   - 如果失败，自动回退到其他数据源（东方财富等）
3. **构建AI上下文** → 包含完整的财务数据
4. **AI分析** → 使用基本面数据进行分析

## 🎯 获取的数据字段

### 盈利能力指标
- ✅ ROE（净资产收益率）
- ✅ 毛利率
- ✅ 销售净利率

### 每股指标
- ✅ EPS（基本每股收益）
- ✅ BPS（每股净资产）

### 财务数据
- ✅ 营业总收入（万元）
- ✅ 净利润（万元）

### 成长性指标
- ✅ 营业收入同比增长率
- ✅ 净利润同比增长率

### 偿债能力
- ✅ 资产负债率
- ✅ 流动比率
- ✅ 速动比率

### 运营能力
- ✅ 存货周转率
- ✅ 应收账款周转率

### 估值指标
- ✅ PE（市盈率）- 从实时行情获取
- ✅ PB（市净率）- 从实时行情获取

## 🚀 使用方法

### 1. 启动服务

确保Python数据服务已启动：

```bash
cd python-data-service
python stock_data_service.py
```

或使用批处理文件：

```bash
start-service.bat
```

### 2. 通过API调用AI分析

```bash
# 使用curl
curl -X POST "http://localhost:5000/api/ai/analyze/000001" \
  -H "Content-Type: application/json" \
  -d '{
    "promptId": null,
    "context": null,
    "modelId": null
  }'
```

### 3. 在前端使用

前端可以直接调用AI分析接口，系统会自动：
- 优先使用AKShare获取基本面数据
- 如果AKShare不可用，自动回退到其他数据源
- 将完整的财务数据传递给AI进行分析

## 🔍 验证集成

### 测试Python服务

```bash
cd python-data-service
python test_akshare_api.py
```

### 测试AI分析功能

1. 确保Python服务运行在 `http://localhost:5001`
2. 确保后端API运行在 `http://localhost:5000`
3. 调用AI分析接口，检查日志输出

## 📝 日志输出示例

成功获取AKShare数据时的日志：

```
[基本面数据] ============================================
[基本面数据] 开始获取股票 000001 的基本面信息
[基本面数据] ============================================
[基本面数据-方案1] 请求Python服务: http://localhost:5001/api/stock/fundamental/000001
[基本面数据-方案1] ✅ 从Python服务(AKShare)获取成功！
[基本面数据-方案1]   数据完整性: 营收=True, 净利润=True, ROE=True, EPS=True
```

AI分析时的日志：

```
[AI分析] ============================================
[AI分析] 开始分析股票: 000001
[AI分析] ============================================
[AI分析] 步骤1: 正在获取股票 000001 的基本面信息（优先使用Python服务/AKShare数据源）...
[AI分析] ✅ 成功获取基本面信息！数据来源: Python服务 (AKShare)
[AI分析]   股票名称: 平安银行
[AI分析]   报告期: 20250930
[AI分析]   营业收入: 10066800.00万元
[AI分析]   净利润: 3833900.00万元
[AI分析]   ROE: 8.28%
[AI分析]   营收增长率: 45.09%
[AI分析]   EPS: 1.870元
```

## ⚙️ 配置

### Python服务地址

默认地址：`http://localhost:5001`

可以通过环境变量修改：

```bash
# Windows PowerShell
$env:PYTHON_DATA_SERVICE_URL="http://localhost:5001"

# Linux/Mac
export PYTHON_DATA_SERVICE_URL="http://localhost:5001"
```

## 🔄 错误处理

系统实现了多层回退机制：

1. **方案1**: Python服务（AKShare） ← 最推荐
2. **方案2**: 东方财富F10详情接口
3. **方案3**: 实时行情接口
4. **方案4**: F10资产负债表接口
5. **方案5**: 财务指标接口
6. **备用方案**: 至少返回PE/PB等基本信息

如果Python服务不可用或返回404，系统会自动尝试其他数据源，确保AI分析功能始终可用。

## ✨ 优势

1. **数据完整**: AKShare提供全面的财务数据
2. **自动回退**: 如果AKShare不可用，自动使用其他数据源
3. **零配置**: 默认配置即可使用
4. **日志完善**: 详细的日志便于调试和监控

## 📚 相关文档

- [AKShare官方文档](https://akshare.akfamily.xyz/)
- [Python数据服务README](README.md)
- [第三方数据源指南](../../THIRD_PARTY_DATA_SOURCES.md)

