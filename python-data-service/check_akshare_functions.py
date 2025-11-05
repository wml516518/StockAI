"""
检查AKShare可用函数
"""
import sys
if sys.platform == 'win32':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except:
        pass

import akshare as ak

print("=" * 80)
print("检查AKShare可用函数")
print("=" * 80)
print()

# 检查AKShare版本
try:
    version = ak.__version__
    print(f"AKShare版本: {version}")
except:
    print("无法获取AKShare版本")
print()

# 检查历史数据相关函数
hist_functions = [
    'stock_zh_a_hist',
    'stock_zh_a_hist_em',
    'stock_zh_a_hist_min_em',
    'stock_zh_a_daily',
    'stock_zh_a_daily_em',
    'tool_trade_date_hist_sina',
]

print("检查历史数据相关函数:")
print("-" * 80)
for func_name in hist_functions:
    if hasattr(ak, func_name):
        print(f"✅ {func_name} - 存在")
        try:
        # 尝试查看函数签名
            func = getattr(ak, func_name)
            import inspect
            sig = inspect.signature(func)
            print(f"   签名: {sig}")
        except:
            pass
    else:
        print(f"❌ {func_name} - 不存在")

print()
print("搜索包含'hist'的函数:")
print("-" * 80)
all_attrs = dir(ak)
hist_attrs = [attr for attr in all_attrs if 'hist' in attr.lower() and not attr.startswith('_')]
for attr in sorted(hist_attrs)[:20]:  # 只显示前20个
    print(f"  {attr}")

print()
print("搜索包含'stock'和'daily'的函数:")
print("-" * 80)
daily_attrs = [attr for attr in all_attrs if 'stock' in attr.lower() and 'daily' in attr.lower() and not attr.startswith('_')]
for attr in sorted(daily_attrs)[:20]:
    print(f"  {attr}")

