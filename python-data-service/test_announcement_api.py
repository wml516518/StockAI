import akshare as ak
import pandas as pd
from datetime import datetime

def test_announcements(symbol="000001"):
    print(f"Testing stock_notice_report for {symbol}...")
    try:
        # stock_notice_report 接口通常需要 symbol (6位代码)
        # 注意：AKShare不同版本接口名称可能不同，这里尝试几个可能的
        
        df = None
        try:
            print("Trying stock_notice_report...")
            df = ak.stock_notice_report(symbol=symbol, date="") # date="" usually gets recent
        except Exception as e:
            print(f"stock_notice_report failed: {e}")
            
        if df is None:
            try:
                print("Trying stock_tease_notice_class...")
                df = ak.stock_tease_notice_class(symbol=symbol, date=datetime.now().strftime("%Y%m%d"))
            except Exception as e:
                print(f"stock_tease_notice_class failed: {e}")

        if df is not None and not df.empty:
            print("Success! Columns:", df.columns.tolist())
            print("First row:", df.iloc[0].to_dict())
            return df
        else:
            print("No data found or all methods failed.")
            return None

    except Exception as e:
        print(f"Global error: {e}")
        return None

if __name__ == "__main__":
    test_announcements("000001")
    test_announcements("600519")
