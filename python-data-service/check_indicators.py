"""
检查stock_financial_abstract中可用的指标名称
"""
import sys
if sys.platform == 'win32':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except:
        pass

import akshare as ak
import pandas as pd

stock_code = "000001"
df = ak.stock_financial_abstract(symbol=stock_code)

print("所有可用的指标名称:")
print("=" * 60)
unique_indicators = df['指标'].unique()
for i, indicator in enumerate(unique_indicators, 1):
    print(f"{i}. {indicator}")

print("\n" + "=" * 60)
print("查找包含'收益'、'毛利率'、'净资产'的指标:")
print("=" * 60)
keywords = ['收益', '毛利率', '净资产', 'ROE', 'roe']
for indicator in unique_indicators:
    for keyword in keywords:
        if keyword in indicator:
            print(f"  - {indicator}")
            break

