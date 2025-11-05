# AKShare历史数据测试说明

## 问题诊断

根据测试结果，您的AKShare版本中**没有** `stock_zh_a_hist_em` 函数。应该使用 `stock_zh_a_hist` 函数。

## 修复内容

1. ✅ 已修复 `test_history_data.py` - 改用 `stock_zh_a_hist`
2. ⚠️ 需要修复 `stock_data_service.py` - 将所有 `stock_zh_a_hist_em` 替换为 `stock_zh_a_hist`

## 使用方法

### 1. 检查AKShare可用函数
```bash
python check_akshare_functions.py
```

### 2. 测试获取历史数据
```bash
# 测试指定股票
python test_history_data.py 300474

# 测试默认股票列表
python test_history_data.py
```

### 3. 函数调用格式

**stock_zh_a_hist** 的正确调用方式：
```python
import akshare as ak

# 方法1: 前复权
df = ak.stock_zh_a_hist(
    symbol="sz300474",  # 注意：需要市场前缀 sh/sz
    period="daily",
    start_date="20240807",
    end_date="20241105",
    adjust="qfq"  # 前复权
)

# 方法2: 无复权
df = ak.stock_zh_a_hist(
    symbol="sz300474",
    period="daily",
    start_date="20240807",
    end_date="20241105",
    adjust=""  # 空字符串表示无复权
)

# 方法3: 后复权
df = ak.stock_zh_a_hist(
    symbol="sz300474",
    period="daily",
    start_date="20240807",
    end_date="20241105",
    adjust="hfq"  # 后复权
)
```

## 重要提示

1. **市场前缀**：
   - 上海股票（6开头）：`sh` + 股票代码，如 `sh600000`
   - 深圳股票（0/3开头）：`sz` + 股票代码，如 `sz300474`

2. **日期格式**：必须是 `YYYYMMDD` 格式，如 `20241105`

3. **如果返回空数据**：
   - 检查股票代码是否正确
   - 检查日期范围是否合理（可能是停牌期间）
   - 尝试更长的日期范围
   - 检查网络连接（AKShare需要访问外部API）

## 下一步

需要更新 `stock_data_service.py` 中的所有 `stock_zh_a_hist_em` 调用，改为使用 `stock_zh_a_hist`。

