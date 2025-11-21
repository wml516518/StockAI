#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""测试 stock_zh_a_hist_tx 接口返回的列结构"""

import sys
import akshare as ak
import pandas as pd

if sys.platform.startswith('win'):
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

def test_stock_zh_a_hist_tx():
    """测试 stock_zh_a_hist_tx 返回的列"""
    symbol = 'sh600745'
    start_date = '20240101'
    end_date = '20241201'
    
    print(f"测试股票: {symbol}")
    print(f"日期范围: {start_date} 到 {end_date}")
    print("-" * 50)
    
    try:
        df = ak.stock_zh_a_hist_tx(
            symbol=symbol,
            start_date=start_date,
            end_date=end_date
        )
        
        if df is None:
            print("返回 None")
            return
        
        if df.empty:
            print("返回空 DataFrame")
            return
        
        print(f"成功获取数据，共 {len(df)} 行")
        print(f"\n列名: {list(df.columns)}")
        print(f"\n数据类型:")
        print(df.dtypes)
        print(f"\n前5行数据:")
        print(df.head())
        print(f"\n后5行数据:")
        print(df.tail())
        
        # 检查是否有 volume 相关的列
        volume_cols = [col for col in df.columns if 'volume' in str(col).lower() or '成交量' in str(col)]
        turnover_cols = [col for col in df.columns if 'turnover' in str(col).lower() or '成交额' in str(col) or 'amount' in str(col).lower()]
        
        print(f"\n成交量相关列: {volume_cols}")
        print(f"成交额相关列: {turnover_cols}")
        
    except Exception as e:
        print(f"错误: {e}")
        import traceback
        traceback.print_exc()

if __name__ == '__main__':
    test_stock_zh_a_hist_tx()

