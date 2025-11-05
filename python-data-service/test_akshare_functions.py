"""
测试AKShare各种财务数据函数
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

def test_function(func_name, stock_code, *args, **kwargs):
    """测试指定的函数"""
    try:
        if not hasattr(ak, func_name):
            return None, f"函数 {func_name} 不存在"
        
        func = getattr(ak, func_name)
        result = func(*args, **kwargs)
        
        if result is None:
            return None, "返回None"
        
        if isinstance(result, pd.DataFrame):
            if result.empty:
                return None, "返回空DataFrame"
            return result, f"成功，共{len(result)}条记录"
        else:
            return result, f"成功，类型: {type(result)}"
    except Exception as e:
        return None, f"错误: {str(e)}"

def main():
    """主测试函数"""
    print("=" * 60)
    print("测试AKShare财务数据函数")
    print(f"测试时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"AKShare版本: {ak.__version__ if hasattr(ak, '__version__') else '未知'}")
    print("=" * 60 + "\n")
    
    stock_code = "000001"  # 平安银行
    print(f"测试股票代码: {stock_code}\n")
    
    # 要测试的函数列表
    functions_to_test = [
        # 财务摘要和指标
        ("stock_financial_abstract", {"symbol": stock_code}),
        ("stock_financial_analysis_indicator", {"symbol": stock_code}),
        ("stock_financial_analysis_indicator_em", {"symbol": stock_code}),
        
        # 利润表相关
        ("stock_lrb_em", {"symbol": stock_code}),
        ("stock_profit_sheet_by_report_em", {"symbol": stock_code}),
        ("stock_profit_em", {"symbol": stock_code}),
        
        # 资产负债表相关
        ("stock_zcfz_em", {"symbol": stock_code}),
        ("stock_balance_sheet_by_report_em", {"symbol": stock_code}),
        ("stock_balance_sheet_em", {"symbol": stock_code}),
        
        # 现金流量表相关
        ("stock_xjll_em", {"symbol": stock_code}),
        ("stock_cash_flow_sheet_by_report_em", {"symbol": stock_code}),
        ("stock_cash_flow_em", {"symbol": stock_code}),
        
        # 其他财务数据
        ("stock_zh_a_hist", {"symbol": stock_code, "period": "daily", "start_date": "20240101", "end_date": "20241101"}),
    ]
    
    success_count = 0
    for func_name, kwargs in functions_to_test:
        print(f"测试: {func_name}")
        result, message = test_function(func_name, stock_code, **kwargs)
        
        if result is not None:
            print(f"  ✅ {message}")
            if isinstance(result, pd.DataFrame):
                print(f"     列数: {len(result.columns)}, 行数: {len(result)}")
                print(f"     列名: {list(result.columns)[:5]}...")  # 只显示前5个列名
                if len(result) > 0:
                    print(f"     最新记录字段数: {len(result.iloc[0])}")
            success_count += 1
        else:
            print(f"  ❌ {message}")
        print()
    
    print("=" * 60)
    print(f"测试完成: {success_count}/{len(functions_to_test)} 个函数成功")
    print("=" * 60)
    
    # 如果找到成功的函数，显示详细数据
    if success_count > 0:
        print("\n尝试获取详细数据...")
        # 尝试使用第一个成功的函数获取详细数据
        for func_name, kwargs in functions_to_test:
            result, message = test_function(func_name, stock_code, **kwargs)
            if result is not None and isinstance(result, pd.DataFrame) and not result.empty:
                print(f"\n使用 {func_name} 获取的数据示例:")
                print(result.head(3).to_string())
                break

if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n测试被用户中断")
    except Exception as e:
        print(f"\n\n❌ 测试过程中发生错误: {str(e)}")
        import traceback
        traceback.print_exc()

