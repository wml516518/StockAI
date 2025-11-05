"""
测试AKShare获取股票近3个月历史交易数据
运行方式: python test_history_data.py [股票代码]
示例: python test_history_data.py 300474
"""
import sys
import os

# 设置Windows控制台编码为UTF-8
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

def test_get_history_data(stock_code):
    """
    测试获取股票历史数据
    
    Args:
        stock_code: 股票代码，如 300474, 000001, 600000
    """
    print("=" * 80)
    print(f"测试获取股票 {stock_code} 的近3个月历史交易数据")
    print("=" * 80)
    print()
    
    # 清理股票代码
    clean_code = stock_code.strip().zfill(6)
    
    # 计算日期范围（3个月）
    end_date = datetime.now()
    start_date = end_date - timedelta(days=3 * 30)
    
    print(f"股票代码: {stock_code} -> {clean_code}")
    print(f"开始日期: {start_date.strftime('%Y-%m-%d')}")
    print(f"结束日期: {end_date.strftime('%Y-%m-%d')}")
    print(f"日期范围: {start_date.strftime('%Y%m%d')} 至 {end_date.strftime('%Y%m%d')}")
    print()
    
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
    
    print(f"市场: {market}")
    print(f"带前缀代码: {symbol}")
    print()
    
    results = []
    
    # 方法1: stock_zh_a_hist (主要方法)
    print("-" * 80)
    print("方法1: stock_zh_a_hist (带市场前缀，前复权)")
    print("-" * 80)
    try:
        print(f"调用参数:")
        print(f"  symbol={symbol}")
        print(f"  period='daily'")
        print(f"  start_date={start_date.strftime('%Y%m%d')}")
        print(f"  end_date={end_date.strftime('%Y%m%d')}")
        print(f"  adjust='qfq'")
        print()
        
        df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                               start_date=start_date.strftime("%Y%m%d"),
                               end_date=end_date.strftime("%Y%m%d"),
                               adjust="qfq")
        
        if df is not None and not df.empty:
            print(f"✅ 成功！获取 {len(df)} 条数据")
            print(f"列名: {list(df.columns)}")
            print()
            print("前5条数据:")
            print(df.head().to_string())
            print()
            print("后5条数据:")
            print(df.tail().to_string())
            print()
            
            # 统计信息
            if '收盘' in df.columns or 'close' in df.columns:
                close_col = '收盘' if '收盘' in df.columns else 'close'
                print(f"价格统计:")
                print(f"  最高价: {df[close_col].max():.2f}")
                print(f"  最低价: {df[close_col].min():.2f}")
                print(f"  平均价: {df[close_col].mean():.2f}")
                print(f"  最新价: {df[close_col].iloc[-1]:.2f}")
                print()
            
            results.append({
                'method': 'stock_zh_a_hist (qfq)',
                'success': True,
                'rows': len(df),
                'columns': list(df.columns),
                'data': df
            })
        else:
            print("❌ 返回空数据")
            results.append({
                'method': 'stock_zh_a_hist (qfq)',
                'success': False,
                'error': '返回空数据'
            })
    except Exception as e:
        error_detail = traceback.format_exc()
        print(f"❌ 失败: {str(e)}")
        print(f"错误详情:")
        print(error_detail)
        print()
        results.append({
            'method': 'stock_zh_a_hist (qfq)',
            'success': False,
            'error': str(e),
            'trace': error_detail
        })
    
    # 方法2: stock_zh_a_hist (无复权)
    print("-" * 80)
    print("方法2: stock_zh_a_hist (无复权)")
    print("-" * 80)
    try:
        print(f"调用参数:")
        print(f"  symbol={symbol}")
        print(f"  period='daily'")
        print(f"  start_date={start_date.strftime('%Y%m%d')}")
        print(f"  end_date={end_date.strftime('%Y%m%d')}")
        print(f"  adjust='' (无复权)")
        print()
        
        df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                               start_date=start_date.strftime("%Y%m%d"),
                               end_date=end_date.strftime("%Y%m%d"),
                               adjust="")
        
        if df is not None and not df.empty:
            print(f"✅ 成功！获取 {len(df)} 条数据")
            print(f"列名: {list(df.columns)}")
            print()
            print("前5条数据:")
            print(df.head().to_string())
            print()
            
            results.append({
                'method': 'stock_zh_a_hist (no adjust)',
                'success': True,
                'rows': len(df),
                'columns': list(df.columns),
                'data': df
            })
        else:
            print("❌ 返回空数据")
            results.append({
                'method': 'stock_zh_a_hist (no adjust)',
                'success': False,
                'error': '返回空数据'
            })
    except Exception as e:
        error_detail = traceback.format_exc()
        print(f"❌ 失败: {str(e)}")
        print(f"错误详情:")
        print(error_detail[:500])  # 只显示前500字符
        print()
        results.append({
            'method': 'stock_zh_a_hist (no adjust)',
            'success': False,
            'error': str(e)
        })
    
    # 方法3: stock_zh_a_hist (尝试不同的adjust参数)
    print("-" * 80)
    print("方法3: stock_zh_a_hist (后复权)")
    print("-" * 80)
    try:
        print(f"调用参数:")
        print(f"  symbol={symbol}")
        print(f"  period='daily'")
        print(f"  start_date={start_date.strftime('%Y%m%d')}")
        print(f"  end_date={end_date.strftime('%Y%m%d')}")
        print(f"  adjust='hfq' (后复权)")
        print()
        
        df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                               start_date=start_date.strftime("%Y%m%d"),
                               end_date=end_date.strftime("%Y%m%d"),
                               adjust="hfq")
        
        if df is not None and not df.empty:
            print(f"✅ 成功！获取 {len(df)} 条数据")
            print(f"列名: {list(df.columns)}")
            print()
            print("前5条数据:")
            print(df.head().to_string())
            print()
            
            results.append({
                'method': 'stock_zh_a_hist (hfq)',
                'success': True,
                'rows': len(df),
                'columns': list(df.columns),
                'data': df
            })
        else:
            print("❌ 返回空数据")
            results.append({
                'method': 'stock_zh_a_hist (hfq)',
                'success': False,
                'error': '返回空数据'
            })
    except Exception as e:
        error_detail = traceback.format_exc()
        print(f"❌ 失败: {str(e)}")
        print(f"错误详情:")
        print(error_detail[:500])
        print()
        results.append({
            'method': 'stock_zh_a_hist (hfq)',
            'success': False,
            'error': str(e)
        })
    
    # 方法4: 尝试更长的日期范围（6个月）
    print("-" * 80)
    print("方法4: stock_zh_a_hist (6个月数据，然后过滤)")
    print("-" * 80)
    try:
        start_date_long = end_date - timedelta(days=6 * 30)
        print(f"调用参数:")
        print(f"  symbol={symbol}")
        print(f"  period='daily'")
        print(f"  start_date={start_date_long.strftime('%Y%m%d')} (6个月前)")
        print(f"  end_date={end_date.strftime('%Y%m%d')}")
        print(f"  adjust='qfq'")
        print()
        
        df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                               start_date=start_date_long.strftime("%Y%m%d"),
                               end_date=end_date.strftime("%Y%m%d"),
                               adjust="qfq")
        
        if df is not None and not df.empty:
            print(f"✅ 获取 {len(df)} 条数据（6个月）")
            
            # 尝试过滤到3个月
            date_col = None
            for col in ['日期', 'date', 'Date', '交易日期']:
                if col in df.columns:
                    date_col = col
                    break
            
            if date_col:
                df[date_col] = pd.to_datetime(df[date_col])
                df_filtered = df[df[date_col] >= start_date]
                print(f"过滤后: {len(df_filtered)} 条数据（3个月）")
                print()
                
                if len(df_filtered) > 0:
                    print("前5条数据:")
                    print(df_filtered.head().to_string())
                    print()
                    
                    results.append({
                        'method': 'stock_zh_a_hist (6个月)',
                        'success': True,
                        'rows': len(df_filtered),
                        'columns': list(df_filtered.columns),
                        'data': df_filtered
                    })
                else:
                    print("❌ 过滤后数据为空")
                    results.append({
                        'method': 'stock_zh_a_hist (6个月)',
                        'success': False,
                        'error': '过滤后数据为空'
                    })
            else:
                print("⚠️ 无法找到日期列进行过滤")
                results.append({
                    'method': 'stock_zh_a_hist (6个月)',
                    'success': True,
                    'rows': len(df),
                    'columns': list(df.columns),
                    'data': df,
                    'note': '无法过滤日期'
                })
        else:
            print("❌ 返回空数据")
            results.append({
                'method': 'stock_zh_a_hist (6个月)',
                'success': False,
                'error': '返回空数据'
            })
    except Exception as e:
        error_detail = traceback.format_exc()
        print(f"❌ 失败: {str(e)}")
        print(f"错误详情:")
        print(error_detail[:500])
        print()
        results.append({
            'method': 'stock_zh_a_hist (6个月)',
            'success': False,
            'error': str(e)
        })
    
    # 总结
    print("=" * 80)
    print("测试总结")
    print("=" * 80)
    success_count = sum(1 for r in results if r.get('success'))
    print(f"成功方法数: {success_count}/{len(results)}")
    print()
    
    for i, result in enumerate(results, 1):
        status = "✅" if result.get('success') else "❌"
        method = result.get('method', 'Unknown')
        if result.get('success'):
            rows = result.get('rows', 0)
            print(f"{status} 方法{i}: {method} - 成功获取 {rows} 条数据")
        else:
            error = result.get('error', '未知错误')
            print(f"{status} 方法{i}: {method} - 失败: {error}")
    
    print()
    
    # 如果至少有一个方法成功，显示最佳结果
    successful_results = [r for r in results if r.get('success')]
    if successful_results:
        best_result = successful_results[0]  # 使用第一个成功的方法
        print("=" * 80)
        print("推荐使用方法")
        print("=" * 80)
        print(f"方法: {best_result['method']}")
        print(f"数据条数: {best_result['rows']}")
        print(f"列名: {best_result['columns']}")
        print()
        
        # 显示数据格式示例
        df_best = best_result['data']
        print("数据格式示例（第一条记录）:")
        print("-" * 80)
        for col in df_best.columns:
            value = df_best.iloc[0][col]
            print(f"  {col}: {value}")
        print()
    else:
        print("=" * 80)
        print("⚠️ 所有方法都失败了！")
        print("=" * 80)
        print("可能的原因:")
        print("1. 股票代码不正确")
        print("2. AKShare版本过旧，需要更新: pip install --upgrade akshare")
        print("3. 网络连接问题，AKShare需要访问外部API")
        print("4. 该股票可能停牌或数据不存在")
        print("5. 日期范围可能有问题")
        print()
    
    return results

def main():
    """主函数"""
    # 默认测试股票代码
    default_stocks = ["300474", "000001", "600000"]
    
    if len(sys.argv) > 1:
        # 使用命令行参数
        stock_code = sys.argv[1]
        test_get_history_data(stock_code)
    else:
        # 测试多个股票
        print("\n" + "=" * 80)
        print("AKShare历史数据获取测试")
        print(f"测试时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        print("=" * 80)
        print()
        print("用法: python test_history_data.py [股票代码]")
        print(f"示例: python test_history_data.py 300474")
        print()
        print(f"将测试默认股票: {', '.join(default_stocks)}")
        print()
        
        for stock_code in default_stocks:
            test_get_history_data(stock_code)
            print("\n" + "=" * 80 + "\n")

if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n测试被用户中断")
    except Exception as e:
        print(f"\n\n❌ 测试过程中发生错误: {str(e)}")
        traceback.print_exc()

