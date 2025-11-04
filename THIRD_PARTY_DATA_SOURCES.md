# 第三方股票数据源指南

本文档列出可用的第三方股票数据源，用于获取股票基本面、财务数据等。

## 🇨🇳 国内数据源

### 1. **Tushare Pro** ⭐⭐⭐⭐⭐
**推荐指数：最高**

- **网址**: https://tushare.pro/
- **特点**:
  - 提供全面的中国A股数据
  - 包括财务数据、基本面、行情、历史数据等
  - 支持Python SDK，易于集成
  - 数据质量高，更新及时

- **免费版限制**:
  - 每分钟5次调用
  - 部分高级数据需要积分

- **集成示例**:
```python
import tushare as ts
ts.set_token('your_token')
pro = ts.pro_api()

# 获取财务数据
df = pro.fina_indicator(ts_code='000001.SZ', period='20231231')
```

- **API调用方式**: 需要注册获取token，通过HTTP API或Python SDK调用

---

### 2. **AKShare** ⭐⭐⭐⭐⭐
**推荐指数：非常高**

- **网址**: https://akshare.akfamily.xyz/
- **特点**:
  - 完全免费开源
  - 提供Python库，无需注册
  - 数据源丰富（东方财富、同花顺、新浪等）
  - 支持财务数据、基本面、行情等

- **集成示例**:
```python
import akshare as ak

# 获取财务指标
stock_financial_analysis_indicator_em_df = ak.stock_financial_analysis_indicator_em(
    symbol="000001"
)
```

- **优势**: 
  - 完全免费
  - 无需注册
  - 多数据源聚合

---

### 3. **同花顺iFind** ⭐⭐⭐⭐
**推荐指数：高**

- **网址**: https://www.51ifind.com/
- **特点**:
  - 专业金融数据平台
  - 提供API接口
  - 数据全面准确
- **限制**: 需要付费订阅

---

### 4. **天聚数行（TianAPI）** ⭐⭐⭐
**推荐指数：中等**

- **网址**: https://www.tianapi.com/
- **特点**:
  - 提供股票行情API
  - 普通会员每天100次免费调用
  - 支持实时行情查询
- **API示例**:
```
https://api.tianapi.com/stock/index?key=YOUR_KEY&num=10
```

---

### 5. **ShowAPI** ⭐⭐⭐
**推荐指数：中等**

- **网址**: https://www.showapi.com/
- **特点**:
  - 提供多种股票数据API
  - 有免费额度
  - 支持历史数据分析

---

## 🌍 国际数据源

### 6. **Alpha Vantage** ⭐⭐⭐⭐
**推荐指数：高**

- **网址**: https://www.alphavantage.co/
- **特点**:
  - 全球股票数据
  - 免费版每天500次调用
  - 提供实时和历史数据
  - 支持RESTful API

- **API示例**:
```
https://www.alphavantage.co/query?function=OVERVIEW&symbol=MSFT&apikey=YOUR_KEY
```

---

### 7. **Yahoo Finance API** ⭐⭐⭐⭐
**推荐指数：高**

- **特点**:
  - 免费使用
  - 全球股票数据
  - 支持多种编程语言
- **注意**: 官方API已停止，但可通过第三方库使用

---

## 📊 推荐方案

### 方案1：Tushare Pro（推荐用于生产环境）
- ✅ 数据质量高
- ✅ 支持全面
- ⚠️ 需要注册和token
- ⚠️ 有调用限制

### 方案2：AKShare（推荐用于开发/学习）
- ✅ 完全免费
- ✅ 无需注册
- ✅ 多数据源
- ⚠️ 依赖Python环境

### 方案3：混合方案
- 使用AKShare作为主要数据源（免费）
- Tushare作为备用数据源（更稳定）
- 实时行情继续使用东方财富接口

## 🔧 集成建议

### 在C#项目中集成Python数据源

如果使用Tushare或AKShare，可以考虑：

1. **通过Python脚本调用**:
   - 创建Python脚本获取数据
   - 将数据保存为JSON
   - C#读取JSON文件

2. **通过HTTP API封装**:
   - 创建Python Flask/FastAPI服务
   - 提供RESTful API
   - C#通过HTTP调用

3. **使用.NET的Python绑定**:
   - 使用IronPython或Python.NET
   - 直接在C#中调用Python代码

### 示例：封装Python服务

```python
# data_service.py
from flask import Flask, jsonify
import akshare as ak

app = Flask(__name__)

@app.route('/api/stock/fundamental/<stock_code>')
def get_fundamental(stock_code):
    try:
        # 获取财务数据
        df = ak.stock_financial_analysis_indicator_em(symbol=stock_code)
        # 转换为JSON
        result = df.to_dict('records')
        return jsonify({'success': True, 'data': result})
    except Exception as e:
        return jsonify({'success': False, 'error': str(e)})
```

然后在C#中调用：
```csharp
var response = await _httpClient.GetStringAsync($"http://localhost:5000/api/stock/fundamental/{stockCode}");
```

## 📝 注意事项

1. **API限制**: 注意各数据源的调用频率限制
2. **数据准确性**: 定期验证数据准确性
3. **备用方案**: 建议实现多数据源回退机制
4. **法律合规**: 确保遵守数据源的使用条款

## 🔗 相关资源

- [Tushare文档](https://tushare.pro/document/2)
- [AKShare文档](https://akshare.akfamily.xyz/)
- [Alpha Vantage文档](https://www.alphavantage.co/documentation/)

