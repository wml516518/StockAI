# 股票数据服务 (Python)

使用AKShare提供股票财务数据的HTTP API服务。

## 安装

```bash
# 安装依赖
pip install -r requirements.txt
```

## 运行

```bash
python stock_data_service.py
```

服务将在 `http://localhost:5001` 启动。

## API接口

### 1. 健康检查
```
GET /health
```

### 2. 获取单个股票基本面数据
```
GET /api/stock/fundamental/{stock_code}
```

示例:
```
GET http://localhost:5001/api/stock/fundamental/000001
```

### 3. 批量获取股票基本面数据
```
POST /api/stock/batch
Content-Type: application/json

{
  "stockCodes": ["000001", "600000"]
}
```

## 返回数据格式

```json
{
  "success": true,
  "data": {
    "stockCode": "000001",
    "stockName": "平安银行",
    "reportDate": "2023-12-31",
    "roe": 12.5,
    "grossProfitMargin": 35.2,
    "netProfitMargin": 25.8,
    "eps": 1.25,
    "bps": 15.6,
    "totalRevenue": 1500000.0,
    "netProfit": 280000.0,
    "revenueGrowthRate": 8.5,
    "profitGrowthRate": 12.3,
    "assetLiabilityRatio": 92.5,
    "currentRatio": 1.2,
    "quickRatio": 0.9,
    "inventoryTurnover": 8.5,
    "accountsReceivableTurnover": 12.3,
    "lastUpdate": "2024-01-01T12:00:00",
    "source": "AKShare"
  }
}
```

## 注意事项

1. 首次运行可能需要下载数据，请耐心等待
2. 如果遇到网络问题，AKShare会自动重试
3. 某些股票可能没有财务数据，会返回404

