"""
直接测试AKShare库，不使用HTTP服务
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
from datetime import datetime

def test_akshare_direct():
    """直接测试AKShare库"""
    print("=" * 60)
    print("直接测试AKShare库")
    print(f"测试时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 60 + "\n")
    
    # 测试股票代码
    test_stocks = ["000001", "600000", "000002", "600519"]
    
    print(f"AKShare版本: {ak.__version__ if hasattr(ak, '__version__') else '未知'}\n")
    
    for stock_code in test_stocks:
        print("=" * 60)
        print(f"测试股票代码: {stock_code}")
        print("=" * 60)
        
        try:
            # 方法1: 财务分析指标
            print("\n方法1: stock_financial_analysis_indicator_em")
            try:
                df = ak.stock_financial_analysis_indicator_em(symbol=stock_code)
                if df is not None and not df.empty:
                    print(f"✅ 成功获取数据，共 {len(df)} 条记录")
                    print(f"列名: {list(df.columns)}")
                    print(f"\n最新一条数据:")
                    latest = df.iloc[0]
                    for col in df.columns:
                        value = latest.get(col, 'N/A')
                        if pd.notna(value):
                            print(f"  {col}: {value}")
                    print()
                    continue
                else:
                    print("❌ 返回空数据")
            except Exception as e:
                print(f"❌ 失败: {str(e)}")
                import traceback
                traceback.print_exc()
            
            # 方法2: 尝试其他接口
            print("\n方法2: stock_individual_info_em (基本信息)")
            try:
                df_info = ak.stock_individual_info_em(symbol=stock_code)
                if df_info is not None and not df_info.empty:
                    print(f"✅ 成功获取基本信息")
                    print(df_info.to_string())
                    print()
                else:
                    print("❌ 返回空数据")
            except Exception as e:
                print(f"❌ 失败: {str(e)}")
            
            # 方法3: 利润表
            print("\n方法3: stock_profit_em (利润表)")
            try:
                if hasattr(ak, 'stock_profit_em'):
                    df_profit = ak.stock_profit_em(symbol=stock_code)
                    if df_profit is not None and not df_profit.empty:
                        print(f"✅ 成功获取利润表数据，共 {len(df_profit)} 条记录")
                        print(f"列名: {list(df_profit.columns)}")
                        print()
                    else:
                        print("❌ 返回空数据")
                else:
                    print("❌ 函数不存在")
            except Exception as e:
                print(f"❌ 失败: {str(e)}")
            
            # 方法4: 资产负债表
            print("\n方法4: stock_balance_sheet_by_report_em (资产负债表)")
            try:
                if hasattr(ak, 'stock_balance_sheet_by_report_em'):
                    df_balance = ak.stock_balance_sheet_by_report_em(symbol=stock_code)
                    if df_balance is not None and not df_balance.empty:
                        print(f"✅ 成功获取资产负债表数据，共 {len(df_balance)} 条记录")
                        print(f"列名: {list(df_balance.columns)}")
                        print()
                    else:
                        print("❌ 返回空数据")
                else:
                    print("❌ 函数不存在")
            except Exception as e:
                print(f"❌ 失败: {str(e)}")
            
            # 方法5: 现金流量表
            print("\n方法5: stock_cash_flow_sheet_by_report_em (现金流量表)")
            try:
                if hasattr(ak, 'stock_cash_flow_sheet_by_report_em'):
                    df_cash = ak.stock_cash_flow_sheet_by_report_em(symbol=stock_code)
                    if df_cash is not None and not df_cash.empty:
                        print(f"✅ 成功获取现金流量表数据，共 {len(df_cash)} 条记录")
                        print(f"列名: {list(df_cash.columns)}")
                        print()
                    else:
                        print("❌ 返回空数据")
                else:
                    print("❌ 函数不存在")
            except Exception as e:
                print(f"❌ 失败: {str(e)}")
                
        except Exception as e:
            print(f"\n❌ 测试 {stock_code} 时发生错误: {str(e)}")
            import traceback
            traceback.print_exc()
        
        print()
    
    print("=" * 60)
    print("测试完成！")
    print("=" * 60)

if __name__ == '__main__':
    try:
        test_akshare_direct()
    except KeyboardInterrupt:
        print("\n\n测试被用户中断")
    except Exception as e:
        print(f"\n\n❌ 测试过程中发生错误: {str(e)}")
        import traceback
        traceback.print_exc()

