"""
测试获取300474（景嘉微）的历史数据
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
from datetime import datetime, timedelta

def test_300474():
    """测试300474的历史数据获取"""
    stock_code = "300474"
    clean_code = stock_code.strip().zfill(6)
    
    # 计算日期范围（3个月）
    end_date = datetime.now()
    start_date = end_date - timedelta(days=3 * 30)
    
    print("=" * 60)
    print(f"测试股票代码: {stock_code}")
    print(f"时间范围: {start_date.date()} 至 {end_date.date()}")
    print("=" * 60 + "\n")
    
    # 方法1: stock_zh_a_hist_em
    print("方法1: stock_zh_a_hist_em (直接使用代码)")
    try:
        df = ak.stock_zh_a_hist_em(symbol=clean_code,
                                 start_date=start_date.strftime("%Y%m%d"),
                                 end_date=end_date.strftime("%Y%m%d"),
                                 adjust="qfq")
        if df is not None and not df.empty:
            print(f"✅ 成功！获取 {len(df)} 条数据")
            print(f"列名: {list(df.columns)}")
            print(f"\n前5条数据:")
            print(df.head().to_string())
            return df
        else:
            print("❌ 返回空数据")
    except Exception as e:
        print(f"❌ 失败: {str(e)}")
        import traceback
        traceback.print_exc()
    
    print("\n" + "-" * 60 + "\n")
    
    # 方法2: stock_zh_a_hist_em (无复权)
    print("方法2: stock_zh_a_hist_em (无复权)")
    try:
        df = ak.stock_zh_a_hist_em(symbol=clean_code,
                                 start_date=start_date.strftime("%Y%m%d"),
                                 end_date=end_date.strftime("%Y%m%d"))
        if df is not None and not df.empty:
            print(f"✅ 成功！获取 {len(df)} 条数据")
            print(f"列名: {list(df.columns)}")
            print(f"\n前5条数据:")
            print(df.head().to_string())
            return df
        else:
            print("❌ 返回空数据")
    except Exception as e:
        print(f"❌ 失败: {str(e)}")
        import traceback
        traceback.print_exc()
    
    print("\n" + "-" * 60 + "\n")
    
    # 方法3: stock_zh_a_hist (带市场前缀)
    print("方法3: stock_zh_a_hist (带市场前缀 sz)")
    try:
        symbol = f"sz{clean_code}"
        df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                               start_date=start_date.strftime("%Y%m%d"),
                               end_date=end_date.strftime("%Y%m%d"),
                               adjust="qfq")
        if df is not None and not df.empty:
            print(f"✅ 成功！获取 {len(df)} 条数据")
            print(f"列名: {list(df.columns)}")
            print(f"\n前5条数据:")
            print(df.head().to_string())
            return df
        else:
            print("❌ 返回空数据")
    except Exception as e:
        print(f"❌ 失败: {str(e)}")
        import traceback
        traceback.print_exc()
    
    print("\n" + "-" * 60 + "\n")
    
    # 方法4: 尝试更长的日期范围
    print("方法4: stock_zh_a_hist_em (6个月数据)")
    try:
        start_date_long = end_date - timedelta(days=6 * 30)
        df = ak.stock_zh_a_hist_em(symbol=clean_code,
                                 start_date=start_date_long.strftime("%Y%m%d"),
                                 end_date=end_date.strftime("%Y%m%d"))
        if df is not None and not df.empty:
            print(f"✅ 成功！获取 {len(df)} 条数据")
            print(f"列名: {list(df.columns)}")
            return df
        else:
            print("❌ 返回空数据")
    except Exception as e:
        print(f"❌ 失败: {str(e)}")
        import traceback
        traceback.print_exc()
    
    print("\n" + "=" * 60)
    print("所有方法都失败")
    print("=" * 60)
    return None

if __name__ == '__main__':
    try:
        test_300474()
    except KeyboardInterrupt:
        print("\n\n测试被用户中断")
    except Exception as e:
        print(f"\n\n❌ 测试过程中发生错误: {str(e)}")
        import traceback
        traceback.print_exc()

