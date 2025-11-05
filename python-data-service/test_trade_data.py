"""
测试AKShare获取股票交易数据（逐笔成交、分时成交、买卖盘口等）
运行方式: python test_trade_data.py [股票代码]
示例: python test_trade_data.py 300474
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
import traceback

def check_available_functions():
    """检查AKShare中与交易数据相关的函数"""
    print("=" * 80)
    print("检查AKShare交易数据相关函数")
    print("=" * 80)
    print()
    
    # 搜索相关函数
    all_attrs = dir(ak)
    
    # 搜索关键词
    keywords = [
        'tick',      # 逐笔成交
        'trade',     # 交易
        'transaction',  # 成交
        'order',     # 委托
        'bid',       # 买
        'ask',       # 卖
        'deal',      # 成交
        'minute',    # 分钟
        'realtime',  # 实时
        'detail',    # 明细
        'flow',      # 资金流
        'big',       # 大单
    ]
    
    found_functions = {}
    for keyword in keywords:
        funcs = [attr for attr in all_attrs 
                if keyword.lower() in attr.lower() 
                and not attr.startswith('_')
                and 'stock' in attr.lower()]
        if funcs:
            found_functions[keyword] = funcs[:10]  # 只显示前10个
    
    print("找到的交易数据相关函数:")
    print("-" * 80)
    for keyword, funcs in found_functions.items():
        print(f"\n包含 '{keyword}' 的函数:")
        for func in funcs:
            print(f"  - {func}")
    
    print()
    return found_functions

def test_realtime_trade_data(stock_code):
    """
    测试获取实时交易数据
    
    Args:
        stock_code: 股票代码，如 300474, 000001, 600000
    """
    print("=" * 80)
    print(f"测试获取股票 {stock_code} 的交易数据")
    print("=" * 80)
    print()
    
    clean_code = stock_code.strip().zfill(6)
    
    # 确定市场前缀
    if clean_code.startswith('6'):
        symbol = f"sh{clean_code}"
        market = "上海"
    elif clean_code.startswith('0') or clean_code.startswith('3'):
        symbol = f"sz{clean_code}"
        market = "深圳"
    else:
        symbol = clean_code
        market = "未知"
    
    print(f"股票代码: {stock_code} -> {clean_code}")
    print(f"市场: {market}")
    print(f"带前缀代码: {symbol}")
    print()
    
    results = []
    
    # 方法1: 实时行情（包含买卖盘口）
    print("-" * 80)
    print("方法1: 实时行情数据（包含买卖盘口）")
    print("-" * 80)
    try:
        # 尝试多个可能的函数名
        func_names = [
            'stock_zh_a_spot_em',
            'stock_realtime_quote',
            'stock_zh_a_spot',
        ]
        
        for func_name in func_names:
            if hasattr(ak, func_name):
                print(f"尝试函数: {func_name}")
                try:
                    df = getattr(ak, func_name)()
                    if df is not None and not df.empty:
                        # 查找指定股票
                        stock_data = df[df['代码'] == clean_code] if '代码' in df.columns else None
                        if stock_data is not None and not stock_data.empty:
                            print(f"✅ 成功！找到股票数据")
                            print(f"列名: {list(df.columns)}")
                            print(f"\n股票数据:")
                            print(stock_data.to_string())
                            print()
                            results.append({
                                'method': f'{func_name} (实时行情)',
                                'success': True,
                                'data': stock_data
                            })
                            break
                        else:
                            print(f"⚠️ 数据获取成功，但未找到股票 {clean_code}")
                    else:
                        print(f"⚠️ 返回空数据")
                except Exception as e:
                    print(f"❌ {func_name} 失败: {str(e)}")
            else:
                print(f"⚠️ 函数 {func_name} 不存在")
    except Exception as e:
        print(f"❌ 方法1失败: {str(e)}")
        traceback.print_exc()
    
    # 方法2: 分时成交数据
    print("-" * 80)
    print("方法2: 分时成交数据（每分钟成交）")
    print("-" * 80)
    try:
        func_names = [
            'stock_zh_a_minute',
            'stock_zh_a_minute_em',
            'stock_minute_em',
        ]
        
        for func_name in func_names:
            if hasattr(ak, func_name):
                print(f"尝试函数: {func_name}")
                try:
                    # 尝试不同的参数格式
                    try:
                        df = getattr(ak, func_name)(symbol=symbol, period="1")
                    except:
                        try:
                            df = getattr(ak, func_name)(symbol=clean_code, period="1")
                        except:
                            df = getattr(ak, func_name)(symbol=symbol)
                    
                    if df is not None and not df.empty:
                        print(f"✅ 成功！获取 {len(df)} 条分时数据")
                        print(f"列名: {list(df.columns)}")
                        print(f"\n前10条数据:")
                        print(df.head(10).to_string())
                        print()
                        results.append({
                            'method': f'{func_name} (分时成交)',
                            'success': True,
                            'rows': len(df),
                            'data': df
                        })
                        break
                    else:
                        print(f"⚠️ 返回空数据")
                except Exception as e:
                    print(f"❌ {func_name} 失败: {str(e)}")
            else:
                print(f"⚠️ 函数 {func_name} 不存在")
    except Exception as e:
        print(f"❌ 方法2失败: {str(e)}")
        traceback.print_exc()
    
    # 方法3: 逐笔成交数据
    print("-" * 80)
    print("方法3: 逐笔成交数据（每笔交易明细）")
    print("-" * 80)
    try:
        func_names = [
            'stock_zh_a_tick_tx',
            'stock_zh_a_tick',
            'stock_tick_em',
            'stock_zh_a_tick_163',
        ]
        
        for func_name in func_names:
            if hasattr(ak, func_name):
                print(f"尝试函数: {func_name}")
                try:
                    # 尝试不同的参数格式
                    today = datetime.now().strftime("%Y%m%d")
                    try:
                        df = getattr(ak, func_name)(symbol=symbol, trade_date=today)
                    except:
                        try:
                            df = getattr(ak, func_name)(symbol=clean_code, trade_date=today)
                        except:
                            try:
                                df = getattr(ak, func_name)(symbol=symbol)
                            except:
                                df = getattr(ak, func_name)(symbol=clean_code)
                    
                    if df is not None and not df.empty:
                        print(f"✅ 成功！获取 {len(df)} 条逐笔数据")
                        print(f"列名: {list(df.columns)}")
                        print(f"\n前10条数据:")
                        print(df.head(10).to_string())
                        print()
                        results.append({
                            'method': f'{func_name} (逐笔成交)',
                            'success': True,
                            'rows': len(df),
                            'data': df
                        })
                        break
                    else:
                        print(f"⚠️ 返回空数据")
                except Exception as e:
                    print(f"❌ {func_name} 失败: {str(e)}")
            else:
                print(f"⚠️ 函数 {func_name} 不存在")
    except Exception as e:
        print(f"❌ 方法3失败: {str(e)}")
        traceback.print_exc()
    
    # 方法4: 大单交易数据
    print("-" * 80)
    print("方法4: 大单交易数据")
    print("-" * 80)
    try:
        func_names = [
            'stock_zh_a_bid_ask_em',
            'stock_lhb_em',
            'stock_bid_ask_em',
        ]
        
        for func_name in func_names:
            if hasattr(ak, func_name):
                print(f"尝试函数: {func_name}")
                try:
                    try:
                        df = getattr(ak, func_name)(symbol=clean_code)
                    except:
                        df = getattr(ak, func_name)(symbol=symbol)
                    
                    if df is not None and not df.empty:
                        print(f"✅ 成功！获取 {len(df)} 条大单数据")
                        print(f"列名: {list(df.columns)}")
                        print(f"\n前10条数据:")
                        print(df.head(10).to_string())
                        print()
                        results.append({
                            'method': f'{func_name} (大单交易)',
                            'success': True,
                            'rows': len(df),
                            'data': df
                        })
                        break
                    else:
                        print(f"⚠️ 返回空数据")
                except Exception as e:
                    print(f"❌ {func_name} 失败: {str(e)}")
            else:
                print(f"⚠️ 函数 {func_name} 不存在")
    except Exception as e:
        print(f"❌ 方法4失败: {str(e)}")
        traceback.print_exc()
    
    # 方法5: 资金流向数据
    print("-" * 80)
    print("方法5: 资金流向数据（主力资金、散户资金等）")
    print("-" * 80)
    try:
        func_names = [
            'stock_fund_flow_individual',
            'stock_individual_fund_flow',
            'stock_fund_flow_em',
        ]
        
        for func_name in func_names:
            if hasattr(ak, func_name):
                print(f"尝试函数: {func_name}")
                try:
                    try:
                        df = getattr(ak, func_name)(symbol=clean_code)
                    except:
                        df = getattr(ak, func_name)(symbol=symbol)
                    
                    if df is not None and not df.empty:
                        print(f"✅ 成功！获取资金流向数据")
                        print(f"列名: {list(df.columns)}")
                        print(f"\n数据:")
                        print(df.to_string())
                        print()
                        results.append({
                            'method': f'{func_name} (资金流向)',
                            'success': True,
                            'rows': len(df),
                            'data': df
                        })
                        break
                    else:
                        print(f"⚠️ 返回空数据")
                except Exception as e:
                    print(f"❌ {func_name} 失败: {str(e)}")
            else:
                print(f"⚠️ 函数 {func_name} 不存在")
    except Exception as e:
        print(f"❌ 方法5失败: {str(e)}")
        traceback.print_exc()
    
    # 总结
    print("=" * 80)
    print("测试总结")
    print("=" * 80)
    success_count = sum(1 for r in results if r.get('success'))
    print(f"成功方法数: {success_count}/{len(results)}")
    print()
    
    if results:
        print("成功的方法:")
        for i, result in enumerate(results, 1):
            if result.get('success'):
                method = result.get('method', 'Unknown')
                rows = result.get('rows', 'N/A')
                print(f"  ✅ {i}. {method} - {rows} 条数据")
    else:
        print("⚠️ 所有方法都失败了！")
        print()
        print("建议:")
        print("1. 检查AKShare版本: pip install --upgrade akshare")
        print("2. 某些数据可能需要交易时间才能获取")
        print("3. 某些数据可能需要VIP权限或付费接口")
    
    print()
    return results

def main():
    """主函数"""
    print("\n" + "=" * 80)
    print("AKShare交易数据获取测试")
    print(f"测试时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 80)
    print()
    
    # 先检查可用函数
    check_available_functions()
    print("\n" + "=" * 80 + "\n")
    
    # 测试股票代码
    if len(sys.argv) > 1:
        stock_code = sys.argv[1]
    else:
        stock_code = "300474"  # 默认测试股票
    
    print(f"将测试股票: {stock_code}")
    print("注意: 某些实时数据仅在交易时间内可用")
    print()
    
    test_realtime_trade_data(stock_code)

if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n测试被用户中断")
    except Exception as e:
        print(f"\n\n❌ 测试过程中发生错误: {str(e)}")
        traceback.print_exc()

