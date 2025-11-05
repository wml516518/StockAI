"""
股票数据服务 - 使用AKShare获取财务数据
运行方式: python stock_data_service.py
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

from flask import Flask, jsonify, request
from flask_cors import CORS
import akshare as ak
import pandas as pd
import numpy as np
import traceback
from datetime import datetime, timedelta

app = Flask(__name__)
CORS(app)  # 允许跨域请求

@app.route('/health', methods=['GET'])
def health():
    """健康检查"""
    return jsonify({'status': 'ok', 'service': 'stock-data-service'})

@app.route('/api/stock/trade/<stock_code>', methods=['GET'])
def get_trade_data(stock_code):
    """
    获取股票交易数据（分时成交、买卖盘口等）
    
    Args:
        stock_code: 股票代码
        data_type: 数据类型，可选值: 'minute'(分时), 'bid_ask'(买卖盘口), 'all'(全部)
    
    Returns:
        JSON格式的交易数据
    """
    try:
        data_type = request.args.get('data_type', 'all')  # 默认获取全部
        clean_code = stock_code.strip().zfill(6)
        
        # 确定市场前缀
        if clean_code.startswith('6'):
            symbol = f"sh{clean_code}"
        else:
            symbol = f"sz{clean_code}"
        
        print(f"[{datetime.now()}] 请求交易数据: {stock_code}, 类型: {data_type}")
        
        result = {
            'stockCode': stock_code,
            'cleanCode': clean_code,
            'symbol': symbol,
            'timestamp': datetime.now().isoformat(),
            'data': {}
        }
        
        # 1. 分时成交数据
        if data_type in ['all', 'minute']:
            try:
                print(f"[{datetime.now()}] 获取分时成交数据...")
                df_minute = ak.stock_zh_a_minute(symbol=symbol, period="1")
                
                if df_minute is not None and not df_minute.empty:
                    # 转换为标准格式
                    minute_data = []
                    for _, row in df_minute.iterrows():
                        minute_data.append({
                            'time': row.get('day', '').strftime("%Y-%m-%d %H:%M:%S") if pd.notna(row.get('day', '')) else '',
                            'open': float(row.get('open', 0)) if pd.notna(row.get('open', 0)) else 0,
                            'high': float(row.get('high', 0)) if pd.notna(row.get('high', 0)) else 0,
                            'low': float(row.get('low', 0)) if pd.notna(row.get('low', 0)) else 0,
                            'close': float(row.get('close', 0)) if pd.notna(row.get('close', 0)) else 0,
                            'volume': float(row.get('volume', 0)) if pd.notna(row.get('volume', 0)) else 0
                        })
                    
                    result['data']['minute'] = {
                        'success': True,
                        'count': len(minute_data),
                        'records': minute_data[-200:] if len(minute_data) > 200 else minute_data  # 只返回最近200条
                    }
                    print(f"[{datetime.now()}] ✅ 分时数据获取成功: {len(minute_data)} 条")
                else:
                    result['data']['minute'] = {'success': False, 'error': '返回空数据'}
            except Exception as e:
                error_msg = str(e)
                print(f"[{datetime.now()}] ⚠️ 分时数据获取失败: {error_msg}")
                result['data']['minute'] = {'success': False, 'error': error_msg}
        
        # 2. 买卖盘口数据
        if data_type in ['all', 'bid_ask']:
            try:
                print(f"[{datetime.now()}] 获取买卖盘口数据...")
                df_bid_ask = ak.stock_bid_ask_em(symbol=clean_code)
                
                if df_bid_ask is not None and not df_bid_ask.empty:
                    # 转换为标准格式
                    bid_ask_data = {}
                    for _, row in df_bid_ask.iterrows():
                        item = row.get('item', '')
                        value = row.get('value', 0)
                        if pd.notna(value):
                            bid_ask_data[item] = float(value)
                    
                    result['data']['bidAsk'] = {
                        'success': True,
                        'data': bid_ask_data
                    }
                    print(f"[{datetime.now()}] ✅ 买卖盘口数据获取成功")
                else:
                    result['data']['bidAsk'] = {'success': False, 'error': '返回空数据'}
            except Exception as e:
                error_msg = str(e)
                print(f"[{datetime.now()}] ⚠️ 买卖盘口数据获取失败: {error_msg}")
                result['data']['bidAsk'] = {'success': False, 'error': error_msg}
        
        return jsonify({'success': True, 'data': result})
        
    except Exception as e:
        error_msg = str(e)
        error_trace = traceback.format_exc()
        print(f"[{datetime.now()}] ❌ 获取交易数据失败: {error_msg}")
        print(error_trace)
        return jsonify({
            'success': False,
            'error': error_msg,
            'trace': error_trace if os.getenv('FLASK_ENV') == 'development' else None
        }), 500

@app.route('/api/test/history/<stock_code>', methods=['GET'])
def test_history_api(stock_code):
    """测试接口：获取股票历史数据（用于诊断）"""
    try:
        months = int(request.args.get('months', 3))
        clean_code = stock_code.strip().zfill(6)
        end_date = datetime.now()
        start_date = end_date - timedelta(days=months * 30)
        
        results = []
        
        # 方法1
        try:
            df1 = ak.stock_zh_a_hist_em(symbol=clean_code,
                                      start_date=start_date.strftime("%Y%m%d"),
                                      end_date=end_date.strftime("%Y%m%d"),
                                      adjust="qfq")
            results.append({
                'method': 'stock_zh_a_hist_em (qfq)',
                'success': df1 is not None and not df1.empty,
                'rows': len(df1) if df1 is not None else 0,
                'columns': list(df1.columns) if df1 is not None and not df1.empty else []
            })
        except Exception as e:
            results.append({
                'method': 'stock_zh_a_hist_em (qfq)',
                'success': False,
                'error': str(e)
            })
        
        # 方法2
        try:
            df2 = ak.stock_zh_a_hist_em(symbol=clean_code,
                                      start_date=start_date.strftime("%Y%m%d"),
                                      end_date=end_date.strftime("%Y%m%d"))
            results.append({
                'method': 'stock_zh_a_hist_em (no adjust)',
                'success': df2 is not None and not df2.empty,
                'rows': len(df2) if df2 is not None else 0,
                'columns': list(df2.columns) if df2 is not None and not df2.empty else []
            })
        except Exception as e:
            results.append({
                'method': 'stock_zh_a_hist_em (no adjust)',
                'success': False,
                'error': str(e)
            })
        
        # 方法3
        if clean_code.startswith('6'):
            symbol = f"sh{clean_code}"
        else:
            symbol = f"sz{clean_code}"
        
        try:
            df3 = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                                   start_date=start_date.strftime("%Y%m%d"),
                                   end_date=end_date.strftime("%Y%m%d"),
                                   adjust="qfq")
            results.append({
                'method': 'stock_zh_a_hist',
                'success': df3 is not None and not df3.empty,
                'rows': len(df3) if df3 is not None else 0,
                'columns': list(df3.columns) if df3 is not None and not df3.empty else []
            })
        except Exception as e:
            results.append({
                'method': 'stock_zh_a_hist',
                'success': False,
                'error': str(e)
            })
        
        return jsonify({
            'success': True,
            'stockCode': stock_code,
            'cleanCode': clean_code,
            'symbol': symbol,
            'months': months,
            'startDate': start_date.strftime("%Y-%m-%d"),
            'endDate': end_date.strftime("%Y-%m-%d"),
            'results': results
        })
    except Exception as e:
        return jsonify({
            'success': False,
            'error': str(e),
            'trace': traceback.format_exc()
        }), 500

@app.route('/api/stock/fundamental/<stock_code>', methods=['GET'])
def get_fundamental(stock_code):
    """
    获取股票基本面数据
    
    Args:
        stock_code: 股票代码，如 000001, 600000
    
    Returns:
        JSON格式的财务数据
    """
    try:
        print(f"[{datetime.now()}] 请求股票基本面数据: {stock_code}")
        
        # 方法1: 使用stock_financial_abstract获取财务摘要（优先方法，稳定可用）
        try:
            clean_code = stock_code.strip().zfill(6)
            print(f"[{datetime.now()}] 方法1: 使用stock_financial_abstract，股票代码: {clean_code}")
            
            # 获取财务摘要数据（返回格式：行是指标，列是日期）
            df = ak.stock_financial_abstract(symbol=clean_code)
            
            if df is None or df.empty:
                print(f"[{datetime.now()}] ⚠️ 方法1: AKShare返回空数据")
                raise ValueError(f"AKShare返回空数据，股票代码 {clean_code} 可能没有财务数据")
            
            # 获取股票基本信息
            try:
                df_info = ak.stock_individual_info_em(symbol=clean_code)
                stock_name = '未知'
                if df_info is not None and not df_info.empty:
                    name_row = df_info[df_info['item'] == '股票简称']
                    if not name_row.empty:
                        stock_name = name_row.iloc[0]['value']
            except:
                stock_name = '未知'
            
            # 找到最新的报告期（第一列是'选项'，第二列是'指标'，后面是日期列）
            date_columns = [col for col in df.columns if col not in ['选项', '指标']]
            if not date_columns:
                raise ValueError("无法找到日期列")
            
            # 获取最新日期（列名格式：YYYYMMDD）
            latest_date_col = sorted(date_columns, reverse=True)[0]
            report_date = latest_date_col
            
            # 定义要提取的指标及其对应的中文字段名
            indicators_map = {
                '归母净利润': 'netProfit',
                '营业总收入': 'totalRevenue',
                '基本每股收益': 'eps',
                '每股净资产': 'bps',
                '净资产收益率(ROE)': 'roe',  # 注意：指标名称包含(ROE)
                '毛利率': 'grossProfitMargin',  # 注意：是"毛利率"而不是"销售毛利率"
                '销售净利率': 'netProfitMargin',
                '资产负债率': 'assetLiabilityRatio',
                '流动比率': 'currentRatio',
                '速动比率': 'quickRatio',
                '存货周转率': 'inventoryTurnover',
                '应收账款周转率': 'accountsReceivableTurnover',
            }
            
            # 从DataFrame中提取数据
            result = {
                'stockCode': stock_code,
                'stockName': stock_name,
                'reportDate': report_date,
                'lastUpdate': datetime.now().isoformat(),
                'source': 'AKShare (stock_financial_abstract)'
            }
            
            # 提取各项指标
            for indicator_name, field_name in indicators_map.items():
                indicator_row = df[df['指标'] == indicator_name]
                if not indicator_row.empty:
                    value = indicator_row.iloc[0][latest_date_col]
                    if pd.notna(value):
                        try:
                            if field_name in ['netProfit', 'totalRevenue']:
                                # 净利润和营业收入转换为万元
                                result[field_name] = float(value) / 10000
                            else:
                                result[field_name] = float(value)
                        except (ValueError, TypeError):
                            result[field_name] = None
                    else:
                        result[field_name] = None
                else:
                    result[field_name] = None
            
            # 计算同比增长率（如果有上一期数据）
            if len(date_columns) >= 2:
                prev_date_col = sorted(date_columns, reverse=True)[1]
                try:
                    # 营业收入同比增长率
                    revenue_row = df[df['指标'] == '营业总收入']
                    if not revenue_row.empty:
                        current_revenue = revenue_row.iloc[0][latest_date_col]
                        prev_revenue = revenue_row.iloc[0][prev_date_col]
                        if pd.notna(current_revenue) and pd.notna(prev_revenue) and prev_revenue != 0:
                            result['revenueGrowthRate'] = ((current_revenue - prev_revenue) / prev_revenue) * 100
                    
                    # 净利润同比增长率
                    profit_row = df[df['指标'] == '归母净利润']
                    if not profit_row.empty:
                        current_profit = profit_row.iloc[0][latest_date_col]
                        prev_profit = profit_row.iloc[0][prev_date_col]
                        if pd.notna(current_profit) and pd.notna(prev_profit) and prev_profit != 0:
                            result['profitGrowthRate'] = ((current_profit - prev_profit) / prev_profit) * 100
                except:
                    pass
            
            print(f"[{datetime.now()}] ✅ 成功获取数据: {stock_code} ({stock_name})")
            return jsonify({'success': True, 'data': result})
        except Exception as e1:
            print(f"[{datetime.now()}] ⚠️ 方法1失败: {str(e1)}")
            print(f"[{datetime.now()}] 错误详情: {traceback.format_exc()}")
        
        # 方法2: 尝试获取利润表数据
        try:
            # 获取利润表数据
            clean_code = stock_code.strip().zfill(6)
            print(f"[{datetime.now()}] 方法2: 尝试使用股票代码: {clean_code}")
            
            # 尝试不同的利润表函数名（AKShare版本可能不同）
            df_profit = None
            try:
                # 尝试新版本函数名
                if hasattr(ak, 'stock_profit_em'):
                    df_profit = ak.stock_profit_em(symbol=clean_code)
                elif hasattr(ak, 'stock_lrb_em'):
                    df_profit = ak.stock_lrb_em(symbol=clean_code)
                elif hasattr(ak, 'stock_profit_sheet_by_report_em'):
                    df_profit = ak.stock_profit_sheet_by_report_em(symbol=clean_code)
            except Exception as e:
                print(f"[{datetime.now()}] ⚠️ 方法2: 无法找到利润表函数: {str(e)}")
            
            if df_profit is None:
                print(f"[{datetime.now()}] ⚠️ 方法2: AKShare返回None或函数不存在")
                raise ValueError("利润表函数不可用或返回None")
            
            if not df_profit.empty:
                latest_profit = df_profit.iloc[0]
                
                result = {
                    'stockCode': stock_code,
                    'totalRevenue': float(latest_profit.get('营业总收入', 0)) / 10000 if pd.notna(latest_profit.get('营业总收入')) else None,
                    'netProfit': float(latest_profit.get('净利润', 0)) / 10000 if pd.notna(latest_profit.get('净利润')) else None,
                    'reportDate': str(latest_profit.get('报告期', '')),
                    'lastUpdate': datetime.now().isoformat(),
                    'source': 'AKShare'
                }
                
                print(f"[{datetime.now()}] ✅ 从利润表获取数据: {stock_code}")
                return jsonify({'success': True, 'data': result})
        except ValueError as e2:
            # 这是预期的错误（数据不可用），不需要详细堆栈
            print(f"[{datetime.now()}] ⚠️ 方法2失败: {str(e2)}")
        except Exception as e2:
            print(f"[{datetime.now()}] ⚠️ 方法2失败: {str(e2)}")
            print(f"[{datetime.now()}] 错误详情: {traceback.format_exc()}")
        
        # 方法3: 尝试其他AKShare接口（资产负债表、现金流量表等）
        try:
            clean_code = stock_code.strip().zfill(6)
            print(f"[{datetime.now()}] 方法3: 尝试获取资产负债表数据: {clean_code}")
            
            # 尝试获取资产负债表
            df_balance = None
            try:
                # 尝试不同的资产负债表函数名
                if hasattr(ak, 'stock_balance_sheet_by_report_em'):
                    df_balance = ak.stock_balance_sheet_by_report_em(symbol=clean_code)
                elif hasattr(ak, 'stock_zcfz_em'):
                    df_balance = ak.stock_zcfz_em(symbol=clean_code)
                elif hasattr(ak, 'stock_balance_sheet_em'):
                    df_balance = ak.stock_balance_sheet_em(symbol=clean_code)
            except Exception as e:
                print(f"[{datetime.now()}] ⚠️ 方法3: 无法找到资产负债表函数: {str(e)}")
            
            if df_balance is None:
                print(f"[{datetime.now()}] ⚠️ 方法3: AKShare返回None或函数不存在")
                raise ValueError("资产负债表函数不可用或返回None")
            
            if not df_balance.empty:
                latest_balance = df_balance.iloc[0]
                
                result = {
                    'stockCode': stock_code,
                    'reportDate': str(latest_balance.get('报告期', '')),
                    'assetLiabilityRatio': float(latest_balance.get('资产负债率', 0)) if pd.notna(latest_balance.get('资产负债率')) else None,
                    'lastUpdate': datetime.now().isoformat(),
                    'source': 'AKShare (资产负债表)'
                }
                
                print(f"[{datetime.now()}] ✅ 从资产负债表获取部分数据: {stock_code}")
                return jsonify({'success': True, 'data': result})
        except ValueError as e3:
            # 这是预期的错误（数据不可用），不需要详细堆栈
            print(f"[{datetime.now()}] ⚠️ 方法3失败: {str(e3)}")
        except Exception as e3:
            print(f"[{datetime.now()}] ⚠️ 方法3失败: {str(e3)}")
            print(f"[{datetime.now()}] 错误详情: {traceback.format_exc()}")
        
        # 如果所有方法都失败，返回详细错误信息
        error_response = {
            'success': False,
            'error': '无法获取财务数据',
            'stockCode': stock_code,
            'message': 'AKShare API无法获取该股票的财务数据，这是AKShare数据源的已知限制',
            'suggestions': [
                '某些股票（特别是创业板300、科创板688等）可能没有完整的财务数据',
                '系统会自动尝试其他数据源（东方财富等）',
                '如需获取数据，请尝试其他股票代码（如：000001, 600000）',
                '可以升级AKShare版本: pip install akshare --upgrade'
            ],
            'note': '这不是系统错误，而是AKShare数据源的限制。系统会自动回退到其他数据源。'
        }
        print(f"[{datetime.now()}] ❌ 所有方法都失败，返回404: {stock_code}")
        return jsonify(error_response), 404
        
    except Exception as e:
        error_msg = str(e)
        error_trace = traceback.format_exc()
        print(f"[{datetime.now()}] ❌ 获取数据失败: {error_msg}")
        print(error_trace)
        return jsonify({
            'success': False,
            'error': error_msg,
            'trace': error_trace
        }), 500

@app.route('/api/stock/history/<stock_code>', methods=['GET'])
def get_history_data(stock_code):
    """
    获取股票历史交易数据（从AKShare获取）
    
    Args:
        stock_code: 股票代码，如 000001, 600000, 300474
        months: 查询月数（默认3个月）
    
    Returns:
        JSON格式的历史交易数据
    """
    try:
        months = int(request.args.get('months', 3))
        print(f"[{datetime.now()}] 请求股票历史数据: {stock_code}, 月数: {months}")
        
        clean_code = stock_code.strip().zfill(6)
        
        # 计算日期范围
        end_date = datetime.now()
        start_date = end_date - timedelta(days=months * 30)
        
        # 确定市场前缀
        if clean_code.startswith('6'):
            symbol = f"sh{clean_code}"
        else:
            symbol = f"sz{clean_code}"
        
        print(f"[{datetime.now()}] 从AKShare获取历史数据: {symbol}, 时间范围: {start_date.date()} 至 {end_date.date()}")
        
        # 使用AKShare获取历史数据
        # 尝试多种AKShare接口
        df = None
        method_used = None
        
        # 方法1: stock_zh_a_hist（主要方法，带市场前缀）
        try:
            print(f"[{datetime.now()}] 尝试方法1: stock_zh_a_hist")
            print(f"[{datetime.now()}] 参数: symbol={symbol}, period=daily, start_date={start_date.strftime('%Y%m%d')}, end_date={end_date.strftime('%Y%m%d')}, adjust=qfq")
            df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                                   start_date=start_date.strftime("%Y%m%d"),
                                   end_date=end_date.strftime("%Y%m%d"),
                                   adjust="qfq")
            if df is not None and not df.empty:
                method_used = "stock_zh_a_hist"
                print(f"[{datetime.now()}] ✅ 方法1成功，获取 {len(df)} 条数据")
            else:
                print(f"[{datetime.now()}] ⚠️ 方法1返回空数据")
        except Exception as e1:
            error_detail = traceback.format_exc()
            print(f"[{datetime.now()}] ⚠️ 方法1失败: {str(e1)}")
            print(f"[{datetime.now()}] 错误详情: {error_detail[:500]}")
        
        # 方法2: stock_zh_a_hist (无复权)
        if df is None or df.empty:
            try:
                print(f"[{datetime.now()}] 尝试方法2: stock_zh_a_hist (无复权)")
                print(f"[{datetime.now()}] 参数: symbol={symbol}, period=daily, start_date={start_date.strftime('%Y%m%d')}, end_date={end_date.strftime('%Y%m%d')}, adjust=''")
                df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                                       start_date=start_date.strftime("%Y%m%d"),
                                       end_date=end_date.strftime("%Y%m%d"),
                                       adjust="")
                if df is not None and not df.empty:
                    method_used = "stock_zh_a_hist (无复权)"
                    print(f"[{datetime.now()}] ✅ 方法2成功，获取 {len(df)} 条数据")
                else:
                    print(f"[{datetime.now()}] ⚠️ 方法2返回空数据")
            except Exception as e2:
                error_detail = traceback.format_exc()
                print(f"[{datetime.now()}] ⚠️ 方法2失败: {str(e2)}")
                print(f"[{datetime.now()}] 错误详情: {error_detail[:500]}")
        
        # 方法3: stock_zh_a_hist (后复权)
        if df is None or df.empty:
            try:
                print(f"[{datetime.now()}] 尝试方法3: stock_zh_a_hist (后复权)")
                print(f"[{datetime.now()}] 参数: symbol={symbol}, period=daily, start_date={start_date.strftime('%Y%m%d')}, end_date={end_date.strftime('%Y%m%d')}, adjust=hfq")
                df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                                       start_date=start_date.strftime("%Y%m%d"),
                                       end_date=end_date.strftime("%Y%m%d"),
                                       adjust="hfq")
                if df is not None and not df.empty:
                    method_used = "stock_zh_a_hist (后复权)"
                    print(f"[{datetime.now()}] ✅ 方法3成功，获取 {len(df)} 条数据")
                else:
                    print(f"[{datetime.now()}] ⚠️ 方法3返回空数据")
            except Exception as e3:
                error_detail = traceback.format_exc()
                print(f"[{datetime.now()}] ⚠️ 方法3失败: {str(e3)}")
                print(f"[{datetime.now()}] 错误详情: {error_detail[:500]}")
        
        # 方法4: 尝试使用更长的日期范围（可能数据不足）
        if df is None or df.empty:
            try:
                print(f"[{datetime.now()}] 尝试方法4: stock_zh_a_hist (6个月数据)")
                start_date_long = end_date - timedelta(days=6 * 30)
                df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                                       start_date=start_date_long.strftime("%Y%m%d"),
                                       end_date=end_date.strftime("%Y%m%d"),
                                       adjust="qfq")
                if df is not None and not df.empty:
                    # 过滤到只保留3个月的数据
                    date_col = None
                    for col in ['日期', 'date', 'Date', '交易日期']:
                        if col in df.columns:
                            date_col = col
                            break
                    if date_col:
                        df[date_col] = pd.to_datetime(df[date_col])
                        df = df[df[date_col] >= start_date]
                    if len(df) > 0:
                        method_used = "stock_zh_a_hist (6个月)"
                        print(f"[{datetime.now()}] ✅ 方法4成功，获取 {len(df)} 条数据")
                    else:
                        df = None
                else:
                    print(f"[{datetime.now()}] ⚠️ 方法4返回空数据")
            except Exception as e4:
                error_detail = traceback.format_exc()
                print(f"[{datetime.now()}] ⚠️ 方法4失败: {str(e4)}")
                print(f"[{datetime.now()}] 错误详情: {error_detail[:500]}")
        
        # 方法5: 如果日线数据都失败，尝试使用分时数据（stock_zh_a_minute）作为补充
        if df is None or df.empty:
            try:
                print(f"[{datetime.now()}] 尝试方法5: stock_zh_a_minute (分时数据作为补充)")
                df_minute = ak.stock_zh_a_minute(symbol=symbol, period="1")
                
                if df_minute is not None and not df_minute.empty:
                    # 将分时数据按日期聚合为日线数据
                    if 'day' in df_minute.columns:
                        df_minute['day'] = pd.to_datetime(df_minute['day'])
                        df_minute['date'] = df_minute['day'].dt.date
                        
                        # 按日期分组，取每日的开盘、最高、最低、收盘、成交量
                        daily_data = df_minute.groupby('date').agg({
                            'open': 'first',      # 开盘价：当日第一条的开盘价
                            'high': 'max',        # 最高价：当日最高
                            'low': 'min',         # 最低价：当日最低
                            'close': 'last',      # 收盘价：当日最后一条的收盘价
                            'volume': 'sum'       # 成交量：当日累计
                        }).reset_index()
                        
                        # 过滤日期范围
                        daily_data = daily_data[daily_data['date'] >= start_date.date()]
                        daily_data = daily_data[daily_data['date'] <= end_date.date()]
                        
                        if len(daily_data) > 0:
                            # 添加成交额（估算：使用收盘价*成交量）
                            daily_data['turnover'] = daily_data['close'] * daily_data['volume']
                            
                            # 重命名列以匹配标准格式
                            daily_data.rename(columns={'date': '日期'}, inplace=True)
                            df = daily_data
                            method_used = "stock_zh_a_minute (分时聚合)"
                            print(f"[{datetime.now()}] ✅ 方法5成功，从分时数据聚合出 {len(df)} 条日线数据")
                        else:
                            df = None
                    else:
                        print(f"[{datetime.now()}] ⚠️ 方法5：分时数据缺少日期列")
                else:
                    print(f"[{datetime.now()}] ⚠️ 方法5返回空数据")
            except Exception as e5:
                error_detail = traceback.format_exc()
                print(f"[{datetime.now()}] ⚠️ 方法5失败: {str(e5)}")
                print(f"[{datetime.now()}] 错误详情: {error_detail[:500]}")
        
        if df is None or df.empty:
            raise ValueError(f"所有AKShare方法都失败，无法获取股票 {clean_code} 的历史数据")
        
        # 转换为标准格式
        result = {
            'stockCode': stock_code,
            'startDate': start_date.strftime("%Y-%m-%d"),
            'endDate': end_date.strftime("%Y-%m-%d"),
            'totalRecords': len(df),
            'method': method_used,
            'data': []
        }
        
        # 转换数据格式（处理不同的列名）
        for _, row in df.iterrows():
            # 尝试多种可能的列名
            date_col = None
            for col_name in ['日期', 'date', 'Date', '交易日期']:
                if col_name in row.index:
                    date_val = row[col_name]
                    if pd.notna(date_val):
                        if isinstance(date_val, str):
                            date_col = date_val
                        else:
                            date_col = date_val.strftime("%Y-%m-%d")
                        break
            
            # 获取价格和成交量数据
            open_val = 0
            close_val = 0
            high_val = 0
            low_val = 0
            volume_val = 0
            turnover_val = 0
            
            # 处理日期（可能是date对象）
            if date_col is None:
                # 尝试从索引中获取日期
                if 'date' in row.index:
                    date_val = row['date']
                    if pd.notna(date_val):
                        if isinstance(date_val, str):
                            date_col = date_val
                        elif hasattr(date_val, 'strftime'):
                            date_col = date_val.strftime("%Y-%m-%d")
                        else:
                            date_col = str(date_val)
            
            for col in ['开盘', 'open', 'Open', '开盘价']:
                if col in row.index and pd.notna(row[col]):
                    open_val = float(row[col])
                    break
            
            for col in ['收盘', 'close', 'Close', '收盘价']:
                if col in row.index and pd.notna(row[col]):
                    close_val = float(row[col])
                    break
            
            for col in ['最高', 'high', 'High', '最高价']:
                if col in row.index and pd.notna(row[col]):
                    high_val = float(row[col])
                    break
            
            for col in ['最低', 'low', 'Low', '最低价']:
                if col in row.index and pd.notna(row[col]):
                    low_val = float(row[col])
                    break
            
            for col in ['成交量', 'volume', 'Volume']:
                if col in row.index and pd.notna(row[col]):
                    volume_val = float(row[col])
                    break
            
            for col in ['成交额', 'amount', 'Amount', '成交金额', 'turnover']:
                if col in row.index and pd.notna(row[col]):
                    turnover_val = float(row[col])
                    break
            
            # 只添加有效数据
            if date_col and close_val > 0:
                result['data'].append({
                    'tradeDate': date_col,
                    'open': open_val,
                    'close': close_val,
                    'high': high_val if high_val > 0 else close_val,
                    'low': low_val if low_val > 0 else close_val,
                    'volume': volume_val,
                    'turnover': turnover_val
                })
        
        if len(result['data']) == 0:
            raise ValueError(f"数据转换失败，无法解析AKShare返回的数据格式")
        
        print(f"[{datetime.now()}] ✅ 成功获取 {len(result['data'])} 条历史数据: {stock_code} (使用方法: {method_used})")
        return jsonify({'success': True, 'data': result})
        
    except Exception as e:
        error_msg = str(e)
        error_trace = traceback.format_exc()
        print(f"[{datetime.now()}] ❌ 获取历史数据失败: {error_msg}")
        print(error_trace)
        return jsonify({
            'success': False,
            'error': error_msg,
            'trace': error_trace
        }), 500

@app.route('/api/stock/analyze/<stock_code>', methods=['GET'])
def analyze_stock_data(stock_code):
    """
    对股票历史数据进行大数据分析（技术指标、趋势分析等）
    
    Args:
        stock_code: 股票代码
        months: 查询月数（默认3个月）
    
    Returns:
        JSON格式的分析结果
    """
    try:
        months = int(request.args.get('months', 3))
        print(f"[{datetime.now()}] 开始分析股票数据: {stock_code}, 月数: {months}")
        
        # 先获取历史数据（直接调用内部逻辑，避免HTTP调用）
        try:
            clean_code = stock_code.strip().zfill(6)
            end_date = datetime.now()
            start_date = end_date - timedelta(days=months * 30)
            
            # 确定市场前缀
            if clean_code.startswith('6'):
                symbol = f"sh{clean_code}"
            else:
                symbol = f"sz{clean_code}"
            
            print(f"[{datetime.now()}] 从AKShare获取历史数据用于分析: {symbol}")
            
            # 尝试获取数据
            df = None
            method_used = None
            
            # 方法1: stock_zh_a_hist_em（东方财富接口，更稳定）
            try:
                print(f"[{datetime.now()}] [分析] 尝试方法1: stock_zh_a_hist_em")
                df = ak.stock_zh_a_hist_em(symbol=clean_code,
                                         start_date=start_date.strftime("%Y%m%d"),
                                         end_date=end_date.strftime("%Y%m%d"),
                                         adjust="qfq")
                if df is not None and not df.empty:
                    method_used = "stock_zh_a_hist_em"
                    print(f"[{datetime.now()}] [分析] ✅ 方法1成功，获取 {len(df)} 条数据")
            except Exception as e1:
                print(f"[{datetime.now()}] [分析] ⚠️ 方法1失败: {str(e1)}")
            
            # 方法2: stock_zh_a_hist_em (无复权)
            if df is None or df.empty:
                try:
                    print(f"[{datetime.now()}] [分析] 尝试方法2: stock_zh_a_hist_em (无复权)")
                    df = ak.stock_zh_a_hist_em(symbol=clean_code,
                                             start_date=start_date.strftime("%Y%m%d"),
                                             end_date=end_date.strftime("%Y%m%d"))
                    if df is not None and not df.empty:
                        method_used = "stock_zh_a_hist_em (无复权)"
                        print(f"[{datetime.now()}] [分析] ✅ 方法2成功，获取 {len(df)} 条数据")
                except Exception as e2:
                    print(f"[{datetime.now()}] [分析] ⚠️ 方法2失败: {str(e2)}")
            
            # 方法3: stock_zh_a_hist（备用，需要市场前缀）
            if df is None or df.empty:
                try:
                    print(f"[{datetime.now()}] [分析] 尝试方法3: stock_zh_a_hist")
                    df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                                           start_date=start_date.strftime("%Y%m%d"),
                                           end_date=end_date.strftime("%Y%m%d"),
                                           adjust="qfq")
                    if df is not None and not df.empty:
                        method_used = "stock_zh_a_hist"
                        print(f"[{datetime.now()}] [分析] ✅ 方法3成功，获取 {len(df)} 条数据")
                except Exception as e3:
                    print(f"[{datetime.now()}] [分析] ⚠️ 方法3失败: {str(e3)}")
            
            # 方法4: 尝试使用更长的日期范围
            if df is None or df.empty:
                try:
                    print(f"[{datetime.now()}] [分析] 尝试方法4: stock_zh_a_hist_em (6个月)")
                    start_date_long = end_date - timedelta(days=6 * 30)
                    df = ak.stock_zh_a_hist_em(symbol=clean_code,
                                             start_date=start_date_long.strftime("%Y%m%d"),
                                             end_date=end_date.strftime("%Y%m%d"))
                    if df is not None and not df.empty:
                        # 过滤到只保留3个月的数据
                        if '日期' in df.columns:
                            df['日期'] = pd.to_datetime(df['日期'])
                            df = df[df['日期'] >= start_date]
                        if len(df) > 0:
                            method_used = "stock_zh_a_hist_em (6个月)"
                            print(f"[{datetime.now()}] [分析] ✅ 方法4成功，获取 {len(df)} 条数据")
                        else:
                            df = None
                except Exception as e4:
                    print(f"[{datetime.now()}] [分析] ⚠️ 方法4失败: {str(e4)}")
            
            if df is None or df.empty:
                return jsonify({
                    'success': False,
                    'error': '无法获取历史数据',
                    'message': f'所有AKShare方法都失败，无法获取股票 {stock_code} 的历史数据'
                }), 500
            
            # 转换数据格式
            history_records = []
            for _, row in df.iterrows():
                date_col = None
                for col_name in ['日期', 'date', 'Date', '交易日期']:
                    if col_name in row.index:
                        date_val = row[col_name]
                        if pd.notna(date_val):
                            if isinstance(date_val, str):
                                date_col = date_val
                            else:
                                date_col = date_val.strftime("%Y-%m-%d")
                            break
                
                # 获取价格数据
                open_val = 0
                close_val = 0
                high_val = 0
                low_val = 0
                volume_val = 0
                turnover_val = 0
                
                for col in ['开盘', 'open', 'Open', '开盘价']:
                    if col in row.index and pd.notna(row[col]):
                        open_val = float(row[col])
                        break
                
                for col in ['收盘', 'close', 'Close', '收盘价']:
                    if col in row.index and pd.notna(row[col]):
                        close_val = float(row[col])
                        break
                
                for col in ['最高', 'high', 'High', '最高价']:
                    if col in row.index and pd.notna(row[col]):
                        high_val = float(row[col])
                        break
                
                for col in ['最低', 'low', 'Low', '最低价']:
                    if col in row.index and pd.notna(row[col]):
                        low_val = float(row[col])
                        break
                
                for col in ['成交量', 'volume', 'Volume']:
                    if col in row.index and pd.notna(row[col]):
                        volume_val = float(row[col])
                        break
                
                for col in ['成交额', 'amount', 'Amount', '成交金额']:
                    if col in row.index and pd.notna(row[col]):
                        turnover_val = float(row[col])
                        break
                
                if date_col and close_val > 0:
                    history_records.append({
                        'tradeDate': date_col,
                        'open': open_val,
                        'close': close_val,
                        'high': high_val if high_val > 0 else close_val,
                        'low': low_val if low_val > 0 else close_val,
                        'volume': volume_val,
                        'turnover': turnover_val
                    })
            
            if len(history_records) == 0:
                return jsonify({
                    'success': False,
                    'error': '历史数据为空或格式不正确'
                }), 500
            
            print(f"[{datetime.now()}] ✅ 成功获取 {len(history_records)} 条历史数据用于分析 (方法: {method_used})")
            
        except Exception as e:
            error_msg = str(e)
            error_trace = traceback.format_exc()
            print(f"[{datetime.now()}] ❌ 获取历史数据失败: {error_msg}")
            print(error_trace)
            return jsonify({
                'success': False,
                'error': '无法获取历史数据',
                'details': error_msg
            }), 500
        
        # 转换为DataFrame进行分析
        df = pd.DataFrame(history_records)
        df['tradeDate'] = pd.to_datetime(df['tradeDate'])
        df = df.sort_values('tradeDate').reset_index(drop=True)
        
        # 验证数据有效性
        if len(df) == 0:
            return jsonify({
                'success': False,
                'error': '历史数据为空，无法进行分析'
            }), 500
        
        # 确保数据列存在
        required_columns = ['close', 'open', 'high', 'low', 'volume', 'turnover']
        for col in required_columns:
            if col not in df.columns:
                return jsonify({
                    'success': False,
                    'error': f'数据缺少必要列: {col}'
                }), 500
        
        # 计算技术指标
        analysis_result = {
            'stockCode': stock_code,
            'analysisDate': datetime.now().isoformat(),
            'period': f"{months}个月",
            'totalRecords': len(df),
            'indicators': {},
            'trends': {},
            'statistics': {},
            'insights': []
        }
        
        # 1. 基础统计
        prices = df['close'].values
        volumes = df['volume'].values
        
        analysis_result['statistics'] = {
            'startPrice': float(prices[0]),
            'endPrice': float(prices[-1]),
            'highestPrice': float(df['high'].max()),
            'lowestPrice': float(df['low'].min()),
            'averagePrice': float(prices.mean()),
            'priceChange': float(prices[-1] - prices[0]),
            'priceChangePercent': float((prices[-1] - prices[0]) / prices[0] * 100),
            'averageVolume': float(volumes.mean()),
            'maxVolume': float(volumes.max()),
            'minVolume': float(volumes.min()),
            'volatility': float(prices.std() / prices.mean() * 100)  # 波动率
        }
        
        # 2. 移动平均线
        df['MA5'] = df['close'].rolling(window=5).mean()
        df['MA10'] = df['close'].rolling(window=10).mean()
        df['MA20'] = df['close'].rolling(window=20).mean()
        df['MA60'] = df['close'].rolling(window=min(60, len(df))).mean()
        
        # 安全获取MA值
        ma5_val = df['MA5'].iloc[-1] if len(df) > 0 and not pd.isna(df['MA5'].iloc[-1]) else None
        ma10_val = df['MA10'].iloc[-1] if len(df) > 0 and not pd.isna(df['MA10'].iloc[-1]) else None
        ma20_val = df['MA20'].iloc[-1] if len(df) > 0 and not pd.isna(df['MA20'].iloc[-1]) else None
        ma60_val = df['MA60'].iloc[-1] if len(df) > 0 and not pd.isna(df['MA60'].iloc[-1]) else None
        
        analysis_result['indicators']['MA'] = {
            'MA5': float(ma5_val) if ma5_val is not None else None,
            'MA10': float(ma10_val) if ma10_val is not None else None,
            'MA20': float(ma20_val) if ma20_val is not None else None,
            'MA60': float(ma60_val) if ma60_val is not None else None,
            'trend': 'up' if ma5_val is not None and ma20_val is not None and ma5_val > ma20_val else 'down'
        }
        
        # 3. MACD指标（需要至少26个数据点）
        if len(df) >= 26:
            exp1 = df['close'].ewm(span=12, adjust=False).mean()
            exp2 = df['close'].ewm(span=26, adjust=False).mean()
            df['MACD'] = exp1 - exp2
            df['Signal'] = df['MACD'].ewm(span=9, adjust=False).mean()
            df['Histogram'] = df['MACD'] - df['Signal']
            
            macd_val = df['MACD'].iloc[-1] if not pd.isna(df['MACD'].iloc[-1]) else None
            signal_val = df['Signal'].iloc[-1] if not pd.isna(df['Signal'].iloc[-1]) else None
            histogram_val = df['Histogram'].iloc[-1] if not pd.isna(df['Histogram'].iloc[-1]) else None
            
            analysis_result['indicators']['MACD'] = {
                'MACD': float(macd_val) if macd_val is not None else None,
                'Signal': float(signal_val) if signal_val is not None else None,
                'Histogram': float(histogram_val) if histogram_val is not None else None,
                'signal': 'bullish' if histogram_val is not None and histogram_val > 0 else 'bearish'
            }
        else:
            analysis_result['indicators']['MACD'] = {
                'MACD': None,
                'Signal': None,
                'Histogram': None,
                'signal': 'insufficient_data'
            }
        
        # 4. RSI指标（需要至少14个数据点）
        if len(df) >= 14:
            delta = df['close'].diff()
            gain = (delta.where(delta > 0, 0)).rolling(window=14).mean()
            loss = (-delta.where(delta < 0, 0)).rolling(window=14).mean()
            # 避免除零错误
            rs = gain / loss.replace([np.inf, -np.inf], np.nan)
            df['RSI'] = 100 - (100 / (1 + rs))
            
            rsi_val = df['RSI'].iloc[-1] if not pd.isna(df['RSI'].iloc[-1]) else None
            
            if rsi_val is not None:
                rsi_signal = 'overbought' if rsi_val > 70 else ('oversold' if rsi_val < 30 else 'neutral')
            else:
                rsi_signal = 'insufficient_data'
            
            analysis_result['indicators']['RSI'] = {
                'RSI': float(rsi_val) if rsi_val is not None else None,
                'signal': rsi_signal
            }
        else:
            analysis_result['indicators']['RSI'] = {
                'RSI': None,
                'signal': 'insufficient_data'
            }
        
        # 5. 布林带（需要至少20个数据点）
        if len(df) >= 20:
            df['BB_Middle'] = df['close'].rolling(window=20).mean()
            bb_std = df['close'].rolling(window=20).std()
            df['BB_Upper'] = df['BB_Middle'] + (bb_std * 2)
            df['BB_Lower'] = df['BB_Middle'] - (bb_std * 2)
            
            bb_upper_val = df['BB_Upper'].iloc[-1] if not pd.isna(df['BB_Upper'].iloc[-1]) else None
            bb_middle_val = df['BB_Middle'].iloc[-1] if not pd.isna(df['BB_Middle'].iloc[-1]) else None
            bb_lower_val = df['BB_Lower'].iloc[-1] if not pd.isna(df['BB_Lower'].iloc[-1]) else None
            
            current_price = prices[-1] if len(prices) > 0 else None
            if current_price is not None and bb_upper_val is not None and bb_lower_val is not None:
                if current_price > bb_upper_val:
                    bb_position = 'above'
                elif current_price < bb_lower_val:
                    bb_position = 'below'
                else:
                    bb_position = 'middle'
            else:
                bb_position = 'insufficient_data'
            
            analysis_result['indicators']['BollingerBands'] = {
                'Upper': float(bb_upper_val) if bb_upper_val is not None else None,
                'Middle': float(bb_middle_val) if bb_middle_val is not None else None,
                'Lower': float(bb_lower_val) if bb_lower_val is not None else None,
                'position': bb_position
            }
        else:
            analysis_result['indicators']['BollingerBands'] = {
                'Upper': None,
                'Middle': None,
                'Lower': None,
                'position': 'insufficient_data'
            }
        
        # 6. 趋势分析
        if len(prices) >= 10:
            recent_10 = prices[-10:]
            early_10 = prices[:10] if len(prices) >= 10 else prices[:len(prices)]
            
            price_trend = 'up' if len(recent_10) > 0 and len(early_10) > 0 and recent_10.mean() > early_10.mean() else 'down'
        else:
            price_trend = 'insufficient_data'
        
        if len(volumes) >= 10:
            recent_vol = volumes[-10:]
            early_vol = volumes[:10] if len(volumes) >= 10 else volumes[:len(volumes)]
            volume_trend = 'increase' if len(recent_vol) > 0 and len(early_vol) > 0 and recent_vol.mean() > early_vol.mean() else 'decrease'
        else:
            volume_trend = 'insufficient_data'
        
        price_change_pct = analysis_result['statistics'].get('priceChangePercent', 0)
        volatility = analysis_result['statistics'].get('volatility', 0)
        
        analysis_result['trends'] = {
            'priceTrend': price_trend,
            'volumeTrend': volume_trend,
            'momentum': 'strong' if abs(price_change_pct) > 10 else 'moderate',
            'volatilityTrend': 'high' if volatility > 5 else 'low'
        }
        
        # 7. 生成洞察
        insights = []
        
        # 价格趋势洞察
        price_trend = analysis_result['trends'].get('priceTrend', 'unknown')
        if price_trend == 'up':
            insights.append("价格整体呈上升趋势")
        elif price_trend == 'down':
            insights.append("价格整体呈下降趋势")
        elif price_trend == 'insufficient_data':
            insights.append("数据不足，无法判断价格趋势")
        
        # MACD信号
        macd_signal = analysis_result['indicators'].get('MACD', {}).get('signal', 'unknown')
        if macd_signal == 'bullish':
            insights.append("MACD指标显示看涨信号")
        elif macd_signal == 'bearish':
            insights.append("MACD指标显示看跌信号")
        elif macd_signal == 'insufficient_data':
            insights.append("数据不足，无法计算MACD指标")
        
        # RSI信号
        rsi_signal = analysis_result['indicators'].get('RSI', {}).get('signal', 'unknown')
        if rsi_signal == 'overbought':
            insights.append("RSI指标显示超买，可能存在回调风险")
        elif rsi_signal == 'oversold':
            insights.append("RSI指标显示超卖，可能存在反弹机会")
        elif rsi_signal == 'neutral':
            insights.append("RSI指标显示中性状态")
        elif rsi_signal == 'insufficient_data':
            insights.append("数据不足，无法计算RSI指标")
        
        # 成交量分析
        volume_trend = analysis_result['trends'].get('volumeTrend', 'unknown')
        if volume_trend == 'increase':
            insights.append("成交量呈放大趋势，市场关注度提升")
        elif volume_trend == 'decrease':
            insights.append("成交量呈萎缩趋势")
        
        # 波动率分析
        volatility_trend = analysis_result['trends'].get('volatilityTrend', 'unknown')
        if volatility_trend == 'high':
            insights.append("股价波动较大，需要注意风险控制")
        elif volatility_trend == 'low':
            insights.append("股价波动较小，相对稳定")
        
        analysis_result['insights'] = insights
        
        print(f"[{datetime.now()}] ✅ 完成数据分析: {stock_code}")
        return jsonify({'success': True, 'data': analysis_result})
        
    except Exception as e:
        error_msg = str(e)
        error_trace = traceback.format_exc()
        print(f"[{datetime.now()}] ❌ 数据分析失败: {error_msg}")
        print(f"[{datetime.now()}] 错误详情:")
        print(error_trace)
        
        # 返回详细的错误信息，但避免暴露敏感信息
        error_response = {
            'success': False,
            'error': error_msg,
            'message': f'分析股票 {stock_code} 时发生错误',
            'hint': '请检查：1. 股票代码是否正确 2. AKShare数据源是否可访问 3. 数据是否完整'
        }
        
        # 只在开发模式下返回详细堆栈
        import os
        if os.getenv('FLASK_ENV') == 'development' or os.getenv('FLASK_DEBUG') == '1':
            error_response['trace'] = error_trace
        
        return jsonify(error_response), 500

@app.route('/api/stock/batch', methods=['POST'])
def get_batch_fundamental():
    """
    批量获取股票基本面数据
    
    Body:
        JSON格式: {"stockCodes": ["000001", "600000"]}
    """
    try:
        data = request.get_json()
        stock_codes = data.get('stockCodes', [])
        
        results = []
        for code in stock_codes:
            try:
                # 调用单个股票接口
                response = get_fundamental(code)
                result_data = response.get_json()
                if result_data.get('success'):
                    results.append(result_data['data'])
            except Exception as e:
                print(f"批量获取失败 {code}: {str(e)}")
                continue
        
        return jsonify({
            'success': True,
            'data': results,
            'count': len(results)
        })
    except Exception as e:
        return jsonify({
            'success': False,
            'error': str(e)
        }), 500

if __name__ == '__main__':
    print("=" * 50)
    print("股票数据服务启动中...")
    print("服务地址: http://localhost:5001")
    print("API文档:")
    print("  GET  /health - 健康检查")
    print("  GET  /api/stock/fundamental/<stock_code> - 获取单个股票基本面")
    print("  GET  /api/stock/history/<stock_code>?months=3 - 获取历史交易数据（AKShare）")
    print("  GET  /api/stock/analyze/<stock_code>?months=3 - 大数据分析（技术指标+趋势）")
    print("  POST /api/stock/batch - 批量获取基本面")
    print("=" * 50)
    
    # 检查是否安装了akshare
    try:
        import pandas as pd
        print("✅ 依赖检查通过")
    except ImportError:
        print("❌ 缺少依赖，请运行: pip install akshare pandas flask flask-cors")
        exit(1)
    
    app.run(host='0.0.0.0', port=5001, debug=True)

