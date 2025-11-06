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
import os
import warnings
import time

# 全局禁用代理以解决连接问题（与测试脚本test_industry_name_em.py保持一致）
# 首先移除所有代理环境变量（与测试脚本保持一致）
original_proxies = {}
for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
    original_proxies[proxy_var] = os.environ.get(proxy_var)
    os.environ.pop(proxy_var, None)

# 然后设置NO_PROXY以禁止所有代理
os.environ['no_proxy'] = '*'
os.environ['NO_PROXY'] = '*'

# 禁用urllib3警告
warnings.filterwarnings('ignore', category=UserWarning)

# 打印代理禁用状态（仅在服务启动时）
print(f"[{datetime.now()}] 🔧 Python服务启动 - 代理设置状态:")
proxy_vars = ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy', 'NO_PROXY', 'no_proxy']
for var in proxy_vars:
    val = os.environ.get(var)
    if val:
        print(f"  {var} = {val[:60]}...")
    else:
        print(f"  {var} = (未设置)")
print(f"[{datetime.now()}] ✅ 已设置 NO_PROXY=* 以禁用代理\n")

# 配置AKShare使用无代理环境（更彻底的代理禁用 - 使用monkey patch）
try:
    import requests
    from requests.adapters import HTTPAdapter
    from requests.packages.urllib3.util.retry import Retry
    import urllib3
    
    # 禁用urllib3警告
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    
    # 保存原始的requests.get和requests.post方法
    _original_get = requests.get
    _original_post = requests.post
    _original_session_init = requests.Session.__init__
    
    # Monkey patch: 拦截所有requests调用，强制禁用代理
    def patched_get(*args, **kwargs):
        # 强制设置proxies=None，确保不使用任何代理
        kwargs['proxies'] = {'http': None, 'https': None}
        kwargs.setdefault('timeout', 30)  # 设置默认超时
        return _original_get(*args, **kwargs)
    
    def patched_post(*args, **kwargs):
        # 强制设置proxies=None，确保不使用任何代理
        kwargs['proxies'] = {'http': None, 'https': None}
        kwargs.setdefault('timeout', 30)  # 设置默认超时
        return _original_post(*args, **kwargs)
    
    # Monkey patch Session类，确保所有Session实例都不使用代理
    def patched_session_init(self, *args, **kwargs):
        _original_session_init(self, *args, **kwargs)
        self.trust_env = False  # 不信任环境变量
        self.proxies = {'http': None, 'https': None}  # 强制禁用代理
    
    # 应用monkey patch
    requests.get = patched_get
    requests.post = patched_post
    requests.Session.__init__ = patched_session_init
    
    # 创建自定义session，完全禁用代理
    def create_no_proxy_session():
        session = requests.Session()
        session.trust_env = False  # 不信任环境变量中的代理设置
        session.proxies = {'http': None, 'https': None}  # 强制禁用代理
        retry_strategy = Retry(
            total=3,
            backoff_factor=0.5,  # 增加重试延迟
            status_forcelist=[429, 500, 502, 503, 504],
        )
        adapter = HTTPAdapter(max_retries=retry_strategy)
        session.mount("http://", adapter)
        session.mount("https://", adapter)
        return session
    
    # 尝试设置环境变量，确保urllib3也不使用代理
    os.environ['REQUESTS_CA_BUNDLE'] = ''
    os.environ['CURL_CA_BUNDLE'] = ''
    
    print(f"[{datetime.now()}] ✅ 已通过monkey patch配置requests库禁用代理（包括系统代理）")
except Exception as e:
    print(f"[{datetime.now()}] ⚠️ 配置requests代理设置时出错: {str(e)}")
    import traceback
    print(traceback.format_exc())
    pass

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
                        # 处理时间字段，确保是datetime对象
                        time_val = row.get('day', '')
                        if pd.notna(time_val):
                            if isinstance(time_val, str):
                                try:
                                    # 尝试将字符串转换为datetime
                                    from datetime import datetime as dt
                                    time_val = pd.to_datetime(time_val)
                                except:
                                    # 如果转换失败，使用原始字符串
                                    time_str = str(time_val)
                            elif hasattr(time_val, 'strftime'):
                                time_str = time_val.strftime("%Y-%m-%d %H:%M:%S")
                            else:
                                time_str = str(time_val)
                        else:
                            time_str = ''
                        
                        minute_data.append({
                            'time': time_str,
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
            
            # 方法1: stock_zh_a_hist（AKShare标准接口）
            try:
                print(f"[{datetime.now()}] [分析] 尝试方法1: stock_zh_a_hist")
                df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                                       start_date=start_date.strftime("%Y%m%d"),
                                       end_date=end_date.strftime("%Y%m%d"),
                                       adjust="qfq")
                if df is not None and not df.empty:
                    method_used = "stock_zh_a_hist"
                    print(f"[{datetime.now()}] [分析] ✅ 方法1成功，获取 {len(df)} 条数据")
            except Exception as e1:
                print(f"[{datetime.now()}] [分析] ⚠️ 方法1失败: {str(e1)}")
            
            # 方法2: stock_zh_a_hist (无复权)
            if df is None or df.empty:
                try:
                    print(f"[{datetime.now()}] [分析] 尝试方法2: stock_zh_a_hist (无复权)")
                    df = ak.stock_zh_a_hist(symbol=symbol, period="daily", 
                                           start_date=start_date.strftime("%Y%m%d"),
                                           end_date=end_date.strftime("%Y%m%d"),
                                           adjust="")
                    if df is not None and not df.empty:
                        method_used = "stock_zh_a_hist (无复权)"
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
                    print(f"[{datetime.now()}] [分析] 尝试方法4: stock_zh_a_hist (6个月)")
                    start_date_long = end_date - timedelta(days=6 * 30)
                    df = ak.stock_zh_a_hist(symbol=symbol, period="daily",
                                           start_date=start_date_long.strftime("%Y%m%d"),
                                           end_date=end_date.strftime("%Y%m%d"),
                                           adjust="qfq")
                    if df is not None and not df.empty:
                        # 过滤到只保留3个月的数据
                        if '日期' in df.columns:
                            df['日期'] = pd.to_datetime(df['日期'])
                            df = df[df['日期'] >= start_date]
                        if len(df) > 0:
                            method_used = "stock_zh_a_hist (6个月)"
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

@app.route('/api/stock/industry/<stock_code>', methods=['GET'])
def get_industry_info(stock_code):
    """
    获取股票所属行业的详情
    
    Args:
        stock_code: 股票代码，如 000001, 600000
    
    Returns:
        JSON格式的行业数据
    """
    # 在函数开始时保存原始代理设置（确保在异常处理中也能访问）
    original_http_proxy = os.environ.get('HTTP_PROXY')
    original_https_proxy = os.environ.get('HTTPS_PROXY')
    original_http_proxy_lower = os.environ.get('http_proxy')
    original_https_proxy_lower = os.environ.get('https_proxy')
    
    try:
        print(f"[{datetime.now()}] 请求股票行业详情: {stock_code}")
        
        clean_code = stock_code.strip().zfill(6)
        
        # 临时移除代理环境变量（在整个函数执行期间禁用代理，与测试脚本保持一致）
        print(f"[{datetime.now()}] 🔧 [行业接口] 再次确认禁用代理设置...")
        for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
            original_value = os.environ.get(proxy_var)
            if original_value:
                print(f"[{datetime.now()}]   - 移除代理: {proxy_var} = {original_value[:50]}...")
            os.environ.pop(proxy_var, None)
        
        # 确保NO_PROXY设置正确
        os.environ['NO_PROXY'] = '*'
        os.environ['no_proxy'] = '*'
        
        # 先尝试从股票基本信息获取行业名称（可选步骤）
        industry_name_from_info = None
        try:
            df_info = None
            max_retries = 2  # 减少重试次数，因为如果失败我们可以用反向查找
            for attempt in range(max_retries):
                try:
                    df_info = ak.stock_individual_info_em(symbol=clean_code)
                    if df_info is not None and not df_info.empty:
                        # 提取行业信息
                        industry_fields = ['所属行业', '行业', '行业分类', '板块']
                        for field in industry_fields:
                            industry_row = df_info[df_info['item'] == field]
                            if not industry_row.empty:
                                industry_name_from_info = str(industry_row.iloc[0]['value']).strip()
                                print(f"[{datetime.now()}] ✅ 从股票信息获取到行业: {industry_name_from_info}")
                                break
                        break
                except Exception as e:
                    error_type = type(e).__name__
                    error_msg = str(e)
                    if attempt < max_retries - 1:
                        print(f"[{datetime.now()}] ⚠️ [行业接口] 获取股票信息失败 (尝试 {attempt + 1}/{max_retries}): {error_type} - {error_msg[:100]}，将使用反向查找...")
                        time.sleep(0.5)
                    else:
                        print(f"[{datetime.now()}] ⚠️ [行业接口] 获取股票信息最终失败 ({error_type})，将使用反向查找")
                        print(f"  错误详情: {error_msg[:200]}")
        except Exception as e:
            print(f"[{datetime.now()}] ⚠️ [行业接口] 获取股票信息异常: {str(e)[:100]}，将使用反向查找")
        
        # 注意：不在此处恢复代理，因为后续还需要调用AKShare函数获取行业板块数据
        # 代理将在函数结束时统一恢复
        
        # 初始化行业信息
        industry_name = industry_name_from_info if industry_name_from_info else '未知'
        industry_code = ''
        
        # 使用 stock_board_industry_name_em 获取所有行业板块，然后匹配
        industry_stocks = []
        industry_performance = {}
        industry_trends = ''
        industry_market_data = {}  # 行业板块市场数据（必须在此初始化，避免后续使用时变量未定义错误）
        
        try:
            # 临时移除代理环境变量（再次确保，与测试脚本保持一致）
            print(f"[{datetime.now()}] 🔧 [行业接口] 禁用代理设置...")
            for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
                original_value = os.environ.get(proxy_var)
                if original_value:
                    print(f"[{datetime.now()}]   - 移除代理: {proxy_var} = {original_value[:50]}...")
                os.environ.pop(proxy_var, None)
            
            # 确保NO_PROXY设置正确（禁止所有代理）
            os.environ['NO_PROXY'] = '*'
            os.environ['no_proxy'] = '*'
            print(f"[{datetime.now()}] ✅ [行业接口] 代理已禁用，NO_PROXY=*")
            
            # 在调用AKShare之前，再次确保禁用代理
            # 尝试通过环境变量和urllib3设置禁用代理
            import urllib3
            urllib3.disable_warnings()
            
            # 获取所有行业板块列表（带重试，增加延迟）
            df_industry_board = None
            for attempt in range(3):
                try:
                    # 每次重试前增加延迟，避免请求过快
                    if attempt > 0:
                        delay = 1.0 * attempt  # 第2次重试延迟1秒，第3次延迟2秒
                        print(f"[{datetime.now()}] ⏳ [行业接口] 等待{delay:.1f}秒后重试...")
                        time.sleep(delay)
                    
                    print(f"[{datetime.now()}] 📡 [行业接口] 尝试调用 stock_board_industry_name_em() (尝试 {attempt + 1}/3)...")
                    start_time = time.time()
                    
                    # 调用AKShare接口
                    df_industry_board = ak.stock_board_industry_name_em()
                    elapsed_time = time.time() - start_time
                    
                    if df_industry_board is not None and not df_industry_board.empty:
                        print(f"[{datetime.now()}] ✅ [行业接口] 成功获取行业板块列表，耗时: {elapsed_time:.2f}秒，共{len(df_industry_board)}个行业")
                        break
                    else:
                        print(f"[{datetime.now()}] ⚠️ [行业接口] 返回数据为空")
                        time.sleep(0.5)
                except Exception as e:
                    error_type = type(e).__name__
                    error_msg = str(e)
                    elapsed_time = time.time() - start_time if 'start_time' in locals() else 0
                    
                    print(f"[{datetime.now()}] ❌ [行业接口] 获取行业板块列表失败 (尝试 {attempt + 1}/3)")
                    print(f"    错误类型: {error_type}")
                    print(f"    错误消息: {error_msg}")
                    print(f"    耗时: {elapsed_time:.2f}秒")
                    
                    # 详细的错误分析
                    print(f"\n    {'='*70}")
                    print(f"    【详细错误诊断】")
                    print(f"    {'='*70}")
                    
                    if 'ConnectionError' in error_type or 'MaxRetriesExceeded' in error_type or 'MaxRetryError' in error_type:
                        print(f"    🔍 错误类型: 网络连接错误")
                        print(f"    - 目标服务器: push2.eastmoney.com (AKShare数据源)")
                        print(f"    - 可能原因:")
                        print(f"      1. 代理服务器不可用或配置错误")
                        print(f"      2. 目标服务器不可达（防火墙/网络限制）")
                        print(f"      3. DNS解析失败")
                        print(f"    - 建议:")
                        print(f"      1. 检查系统代理设置")
                        print(f"      2. 尝试直接访问目标服务器")
                        print(f"      3. 检查防火墙规则")
                    elif 'ProtocolError' in error_type:
                        print(f"    🔍 错误类型: 协议错误")
                        print(f"    - 连接被远程端关闭")
                        print(f"    - 可能原因:")
                        print(f"      1. 请求频率过快，被服务器限制")
                        print(f"      2. 代理服务器问题")
                        print(f"      3. 服务器负载过高，主动断开连接")
                        print(f"    - 建议:")
                        print(f"      1. 增加请求间隔时间（当前已设置0.3-1秒延迟）")
                        print(f"      2. 检查代理配置")
                        print(f"      3. 稍后重试")
                    elif 'RemoteDisconnected' in error_msg:
                        print(f"    🔍 错误类型: 远程连接断开")
                        print(f"    - 服务器主动关闭连接")
                        print(f"    - 可能原因:")
                        print(f"      1. 服务器检测到异常请求")
                        print(f"      2. 网络不稳定导致连接中断")
                        print(f"      3. 代理服务器问题")
                    elif 'Timeout' in error_type:
                        print(f"    🔍 错误类型: 请求超时")
                        print(f"    - 服务器响应过慢或未响应")
                        print(f"    - 建议: 增加超时时间或检查网络")
                    else:
                        print(f"    🔍 错误类型: {error_type}")
                    
                    # 代理状态检查
                    print(f"\n    【代理状态检查】")
                    proxy_found = False
                    for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
                        value = os.environ.get(proxy_var)
                        if value:
                            print(f"    ⚠️ 发现代理设置: {proxy_var} = {value[:60]}...")
                            proxy_found = True
                        else:
                            print(f"    ✅ {proxy_var}: 未设置")
                    
                    if not proxy_found:
                        print(f"    ✅ 所有代理环境变量已清除")
                    
                    # 网络连接测试
                    print(f"\n    【网络连接测试】")
                    try:
                        import socket
                        test_hosts = [
                            ('17.push2.eastmoney.com', 443, '行业板块服务器'),
                            ('push2.eastmoney.com', 443, 'AKShare主服务器'),
                            ('www.baidu.com', 80, '测试基本网络')
                        ]
                        for host, port, desc in test_hosts:
                            try:
                                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                                sock.settimeout(3)
                                result = sock.connect_ex((host, port))
                                sock.close()
                                if result == 0:
                                    print(f"    ✅ {desc}: {host}:{port} - 可连接")
                                else:
                                    print(f"    ❌ {desc}: {host}:{port} - 连接失败 (错误代码: {result})")
                            except Exception as socket_e:
                                print(f"    ❌ {desc}: {host}:{port} - 测试异常: {str(socket_e)[:60]}")
                    except Exception as net_test_e:
                        print(f"    ❌ 网络测试模块异常: {str(net_test_e)[:60]}")
                    
                    # 打印完整的异常堆栈（仅在最后一次尝试时）
                    if attempt >= 2:
                        print(f"\n    【完整错误堆栈】")
                        import traceback
                        full_trace = traceback.format_exc()
                        print(f"    {full_trace[:1000]}")
                    
                    print(f"    {'='*70}\n")
                    
                    if attempt < 2:
                        print(f"    ⏳ 等待1秒后重试...")
                        time.sleep(1)
                    else:
                        print(f"[{datetime.now()}] ❌ [行业接口] 获取行业板块列表最终失败，将返回基础行业信息")
                        df_industry_board = None  # 不抛出异常，允许继续执行
                        break
            if df_industry_board is not None and not df_industry_board.empty:
                # 查找匹配的行业（精确匹配或包含匹配）
                matched_industry = None
                
                # 先尝试精确匹配
                if industry_name and industry_name != '未知':
                    matched_industry = df_industry_board[df_industry_board['板块名称'] == industry_name]
                
                # 如果精确匹配失败，尝试包含匹配
                if (matched_industry is None or matched_industry.empty) and industry_name and industry_name != '未知':
                    matched_industry = df_industry_board[df_industry_board['板块名称'].str.contains(industry_name, na=False)]
                
                # 如果仍然没有匹配，尝试使用股票代码反向查找（限制查找数量以提高性能）
                if (matched_industry is None or matched_industry.empty):
                    print(f"[{datetime.now()}] 通过成分股反向查找行业板块...")
                    max_search = 30  # 最多查找30个行业板块
                    for idx, row in df_industry_board.head(max_search).iterrows():
                        test_industry_code = row.get('板块代码', '')
                        test_industry_name = row.get('板块名称', '')
                        
                        if not test_industry_code:
                            continue
                        
                        try:
                            # 获取该行业的成分股（带重试和延迟）
                            df_test_stocks = None
                            for retry in range(2):
                                try:
                                    time.sleep(0.3)  # 添加延迟，避免请求过快
                                    df_test_stocks = ak.stock_board_industry_cons_em(symbol=test_industry_code)
                                    if df_test_stocks is not None and not df_test_stocks.empty:
                                        break
                                except Exception as e:
                                    if retry < 1:
                                        time.sleep(0.5)
                                    else:
                                        raise
                            
                            if df_test_stocks is not None and not df_test_stocks.empty:
                                stock_codes_in_industry = df_test_stocks['代码'].astype(str).str.zfill(6)
                                if clean_code in stock_codes_in_industry.values:
                                    industry_name = test_industry_name
                                    industry_code = test_industry_code
                                    matched_industry = df_industry_board[df_industry_board['板块代码'] == test_industry_code]
                                    print(f"[{datetime.now()}] ✅ 通过反向查找找到行业: {industry_name} ({industry_code})")
                                    
                                    # 提取行业板块的市场数据（反向查找路径）
                                    matched_row = matched_industry.iloc[0]
                                    try:
                                        # 重新初始化，覆盖之前的空字典
                                        industry_market_data = {}
                                        latest_price = matched_row.get('最新价', None)
                                        change_percent = matched_row.get('涨跌幅', None)
                                        total_market_cap = matched_row.get('总市值', None)
                                        change_amount = matched_row.get('涨跌额', None)
                                        turnover_rate = matched_row.get('换手率', None)
                                        rising_count = matched_row.get('上涨家数', None)
                                        falling_count = matched_row.get('下跌家数', None)
                                        leader_stock = matched_row.get('领涨股票', None)
                                        leader_change_percent = matched_row.get('领涨股票-涨跌幅', None)
                                        
                                        if pd.notna(latest_price):
                                            industry_market_data['latestPrice'] = float(latest_price)
                                        if pd.notna(change_amount):
                                            industry_market_data['changeAmount'] = float(change_amount)
                                        if pd.notna(change_percent):
                                            industry_market_data['changePercent'] = float(change_percent)
                                        if pd.notna(total_market_cap):
                                            industry_market_data['totalMarketCap'] = float(total_market_cap)
                                        if pd.notna(turnover_rate):
                                            industry_market_data['turnoverRate'] = float(turnover_rate)
                                        if pd.notna(rising_count):
                                            industry_market_data['risingCount'] = int(rising_count)
                                        if pd.notna(falling_count):
                                            industry_market_data['fallingCount'] = int(falling_count)
                                        if pd.notna(leader_stock):
                                            industry_market_data['leaderStock'] = str(leader_stock)
                                        if pd.notna(leader_change_percent):
                                            industry_market_data['leaderChangePercent'] = float(leader_change_percent)
                                        
                                        # 构建行业趋势描述
                                        trend_parts = []
                                        if industry_market_data.get('changePercent') is not None:
                                            trend_parts.append(f"行业板块涨跌幅：{industry_market_data['changePercent']:.2f}%")
                                        if industry_market_data.get('totalMarketCap') is not None:
                                            market_cap_billion = industry_market_data['totalMarketCap'] / 1000000000
                                            trend_parts.append(f"总市值：{market_cap_billion:.2f}亿元")
                                        if trend_parts:
                                            industry_trends = "；".join(trend_parts)
                                    except Exception as e:
                                        print(f"[{datetime.now()}] ⚠️ 反向查找路径提取行业板块市场数据失败: {str(e)}")
                                    
                                    break
                        except Exception as e:
                            # 某些行业可能无法获取成分股，跳过
                            continue
                else:
                    # 使用匹配到的行业
                    matched_row = matched_industry.iloc[0]
                    industry_code = matched_row.get('板块代码', '')
                    if not industry_name or industry_name == '未知':
                        industry_name = matched_row.get('板块名称', '未知')
                    
                    # 提取行业板块的完整信息（从stock_board_industry_name_em返回的数据）
                    industry_trends = ""
                    industry_market_data = {}
                    try:
                        # 获取行业板块的市场数据
                        latest_price = matched_row.get('最新价', None)
                        change_amount = matched_row.get('涨跌额', None)
                        change_percent = matched_row.get('涨跌幅', None)
                        total_market_cap = matched_row.get('总市值', None)
                        turnover_rate = matched_row.get('换手率', None)
                        rising_count = matched_row.get('上涨家数', None)
                        falling_count = matched_row.get('下跌家数', None)
                        leader_stock = matched_row.get('领涨股票', None)
                        leader_change_percent = matched_row.get('领涨股票-涨跌幅', None)
                        
                        industry_market_data = {
                            'latestPrice': float(latest_price) if pd.notna(latest_price) else None,
                            'changeAmount': float(change_amount) if pd.notna(change_amount) else None,
                            'changePercent': float(change_percent) if pd.notna(change_percent) else None,
                            'totalMarketCap': float(total_market_cap) if pd.notna(total_market_cap) else None,
                            'turnoverRate': float(turnover_rate) if pd.notna(turnover_rate) else None,
                            'risingCount': int(rising_count) if pd.notna(rising_count) else None,
                            'fallingCount': int(falling_count) if pd.notna(falling_count) else None,
                            'leaderStock': str(leader_stock) if pd.notna(leader_stock) else None,
                            'leaderChangePercent': float(leader_change_percent) if pd.notna(leader_change_percent) else None
                        }
                        
                        # 构建行业趋势描述
                        trend_parts = []
                        if industry_market_data.get('changePercent') is not None:
                            trend_parts.append(f"行业板块涨跌幅：{industry_market_data['changePercent']:.2f}%")
                        if industry_market_data.get('totalMarketCap') is not None:
                            market_cap_billion = industry_market_data['totalMarketCap'] / 1000000000
                            trend_parts.append(f"总市值：{market_cap_billion:.2f}亿元")
                        if industry_market_data.get('risingCount') is not None and industry_market_data.get('fallingCount') is not None:
                            trend_parts.append(f"上涨家数：{industry_market_data['risingCount']}，下跌家数：{industry_market_data['fallingCount']}")
                        if industry_market_data.get('leaderStock'):
                            leader_info = f"领涨股票：{industry_market_data['leaderStock']}"
                            if industry_market_data.get('leaderChangePercent') is not None:
                                leader_info += f"（涨跌幅：{industry_market_data['leaderChangePercent']:.2f}%）"
                            trend_parts.append(leader_info)
                        
                        if trend_parts:
                            industry_trends = "；".join(trend_parts)
                            print(f"[{datetime.now()}] ✅ 成功提取行业板块市场数据")
                    except Exception as e:
                        print(f"[{datetime.now()}] ⚠️ 提取行业板块市场数据失败: {str(e)}")
                
                # 获取行业成分股和表现数据
                if industry_code:
                    try:
                        # 获取行业成分股（带重试和延迟）
                        df_industry_stocks = None
                        for retry in range(3):
                            try:
                                time.sleep(0.3)  # 添加延迟
                                df_industry_stocks = ak.stock_board_industry_cons_em(symbol=industry_code)
                                if df_industry_stocks is not None and not df_industry_stocks.empty:
                                    break
                            except Exception as e:
                                if retry < 2:
                                    print(f"[{datetime.now()}] ⚠️ 获取行业成分股失败 (尝试 {retry + 1}/3): {str(e)[:80]}，重试中...")
                                    time.sleep(1)
                                else:
                                    raise
                        if df_industry_stocks is not None and not df_industry_stocks.empty:
                            # 转换成分股列表
                            for idx, row in df_industry_stocks.head(20).iterrows():  # 最多20只
                                stock_code_industry = str(row.get('代码', '')).zfill(6)
                                stock_name_industry = str(row.get('名称', ''))
                                stock_price = row.get('最新价', 0)
                                stock_change = row.get('涨跌幅', 0)
                                
                                if pd.notna(stock_price) and pd.notna(stock_change):
                                    industry_stocks.append({
                                        'code': stock_code_industry,
                                        'name': stock_name_industry,
                                        'price': float(stock_price) if pd.notna(stock_price) else 0,
                                        'changePercent': float(stock_change) if pd.notna(stock_change) else 0
                                    })
                            
                            # 计算行业平均表现指标（字段名需与C#代码期望的一致）
                            if len(industry_stocks) > 0:
                                prices = [s['price'] for s in industry_stocks if s['price'] > 0]
                                changes = [s['changePercent'] for s in industry_stocks if s['changePercent'] != 0]
                                
                                if prices and changes:
                                    industry_performance = {
                                        'avgPE': None,  # PE需要从个股数据中计算，暂时不提供
                                        'avgPB': None,  # PB需要从个股数据中计算，暂时不提供
                                        'avgROE': None,  # ROE需要从财务数据中获取，暂时不提供
                                        'totalMarketCap': None,  # 总市值需要计算所有个股市值，暂时不提供
                                        'avgChangePercent': round(sum(changes) / len(changes), 2) if changes else 0,
                                        'stockCount': len(industry_stocks),  # 额外字段，股票数量
                                        'avgPrice': round(sum(prices) / len(prices), 2) if prices else 0  # 额外字段，平均价格
                                    }
                            
                            print(f"[{datetime.now()}] ✅ 成功获取行业成分股: {industry_name} ({industry_code})，共{len(industry_stocks)}只股票")
                    except Exception as e:
                        print(f"[{datetime.now()}] ⚠️ 获取行业成分股失败: {str(e)}")
        except Exception as e:
            error_type = type(e).__name__
            error_msg = str(e)
            print(f"[{datetime.now()}] ⚠️ [行业接口] 获取行业板块列表异常: {error_type}")
            print(f"  错误消息: {error_msg[:300]}")
            print(f"  完整堆栈: {traceback.format_exc()[:500]}")
            # 不抛出异常，继续执行
        
        # 构建返回结果（确保字段名与后端期望一致）
        result = {
            'stockCode': stock_code,
            'industryName': industry_name,
            'industryCode': industry_code,
            'description': f'该股票属于{industry_name}行业' if industry_name != '未知' else '无法确定行业信息',
            'stocks': industry_stocks,
            'performance': industry_performance if industry_performance else {},
            'trends': industry_trends if industry_trends else '',
            'marketData': industry_market_data if industry_market_data else {},  # 新增：行业板块市场数据
            'lastUpdate': datetime.now().isoformat(),
            'source': 'AKShare'
        }
        
        print(f"[{datetime.now()}] ✅ 成功获取行业信息: {stock_code} - {industry_name} (代码: {industry_code}, 股票数: {len(industry_stocks)})")
        
        # 恢复原始代理设置（如果有）
        if original_http_proxy:
            os.environ['HTTP_PROXY'] = original_http_proxy
        if original_https_proxy:
            os.environ['HTTPS_PROXY'] = original_https_proxy
        if original_http_proxy_lower:
            os.environ['http_proxy'] = original_http_proxy_lower
        if original_https_proxy_lower:
            os.environ['https_proxy'] = original_https_proxy_lower
        
        return jsonify({'success': True, 'data': result})
        
    except Exception as e:
        error_msg = str(e)
        error_trace = traceback.format_exc()
        print(f"[{datetime.now()}] ❌ 获取行业信息失败: {error_msg}")
        print(error_trace)
        
        # 确保在异常情况下也恢复代理设置
        if original_http_proxy:
            os.environ['HTTP_PROXY'] = original_http_proxy
        if original_https_proxy:
            os.environ['HTTPS_PROXY'] = original_https_proxy
        if original_http_proxy_lower:
            os.environ['http_proxy'] = original_http_proxy_lower
        if original_https_proxy_lower:
            os.environ['https_proxy'] = original_https_proxy_lower
        
        return jsonify({
            'success': False,
            'error': error_msg,
            'message': f'无法获取股票 {stock_code} 的行业信息',
            'trace': error_trace if os.getenv('FLASK_ENV') == 'development' else None
        }), 500

@app.route('/api/stock/hot-rank', methods=['GET'])
def get_hot_rank():
    """
    获取个股人气榜最新排名（使用AKShare的stock_hot_rank_latest_em）
    
    Returns:
        JSON格式的个股人气榜数据
    """
    try:
        print(f"[{datetime.now()}] 请求个股人气榜数据")
        
        # 临时禁用代理设置
        original_http_proxy = os.environ.get('HTTP_PROXY')
        original_https_proxy = os.environ.get('HTTPS_PROXY')
        original_http_proxy_lower = os.environ.get('http_proxy')
        original_https_proxy_lower = os.environ.get('https_proxy')
        
        # 临时移除代理环境变量
        for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
            os.environ.pop(proxy_var, None)
        
        # 确保NO_PROXY设置正确
        os.environ['NO_PROXY'] = '*'
        os.environ['no_proxy'] = '*'
        
        hot_rank_list = []
        
        try:
            print(f"[{datetime.now()}] 🔧 [人气榜接口] 禁用代理设置...")
            for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
                original_value = os.environ.get(proxy_var)
                if original_value:
                    print(f"[{datetime.now()}]   - 移除代理: {proxy_var} = {original_value[:50]}...")
                os.environ.pop(proxy_var, None)
            
            os.environ['NO_PROXY'] = '*'
            os.environ['no_proxy'] = '*'
            print(f"[{datetime.now()}] ✅ [人气榜接口] 代理已禁用，NO_PROXY=*")
            
            import urllib3
            urllib3.disable_warnings()
            
            # 调用AKShare的stock_hot_rank_latest_em接口（带重试）
            df_hot_rank = None
            for attempt in range(3):
                try:
                    if attempt > 0:
                        delay = 1.0 * attempt
                        print(f"[{datetime.now()}] ⏳ [人气榜接口] 等待{delay:.1f}秒后重试...")
                        time.sleep(delay)
                    
                    print(f"[{datetime.now()}] 📡 [人气榜接口] 尝试调用 stock_hot_rank_latest_em() (尝试 {attempt + 1}/3)...")
                    start_time = time.time()
                    
                    # 调用AKShare接口
                    df_hot_rank = ak.stock_hot_rank_latest_em()
                    elapsed_time = time.time() - start_time
                    
                    if df_hot_rank is not None and not df_hot_rank.empty:
                        print(f"[{datetime.now()}] ✅ [人气榜接口] 成功获取个股人气榜数据，耗时: {elapsed_time:.2f}秒，共{len(df_hot_rank)}条")
                        break
                    else:
                        print(f"[{datetime.now()}] ⚠️ [人气榜接口] 返回数据为空")
                        time.sleep(0.5)
                except Exception as e:
                    error_type = type(e).__name__
                    error_msg = str(e)
                    elapsed_time = time.time() - start_time if 'start_time' in locals() else 0
                    
                    print(f"[{datetime.now()}] ❌ [人气榜接口] 获取人气榜数据失败 (尝试 {attempt + 1}/3)")
                    print(f"    错误类型: {error_type}")
                    print(f"    错误消息: {error_msg[:200]}")
                    print(f"    耗时: {elapsed_time:.2f}秒")
                    
                    if attempt < 2:
                        time.sleep(1)
                    else:
                        print(f"[{datetime.now()}] ❌ [人气榜接口] 获取人气榜数据最终失败")
                        df_hot_rank = None
                        break
            
            if df_hot_rank is not None and not df_hot_rank.empty:
                # 解析数据并构建返回格式
                # 根据AKShare的stock_hot_rank_latest_em返回的列名，常见的有：代码、名称、最新价、涨跌幅、成交量、成交额等
                for idx, row in df_hot_rank.iterrows():
                    try:
                        # 尝试不同的列名（AKShare可能返回不同的列名）
                        code = str(row.get('代码', row.get('股票代码', ''))).strip()
                        name = str(row.get('名称', row.get('股票名称', ''))).strip()
                        
                        # 价格相关字段
                        price = row.get('最新价', row.get('现价', row.get('价格', 0)))
                        if pd.isna(price):
                            price = 0
                        
                        # 涨跌幅
                        change_percent = row.get('涨跌幅', row.get('涨幅', 0))
                        if pd.isna(change_percent):
                            change_percent = 0
                        
                        # 成交量
                        volume = row.get('成交量', row.get('成交额', 0))
                        if pd.isna(volume):
                            volume = 0
                        
                        # 成交额
                        turnover = row.get('成交额', row.get('成交金额', 0))
                        if pd.isna(turnover):
                            turnover = 0
                        
                        hot_rank_list.append({
                            'rank': idx + 1,
                            'code': code,
                            'name': name,
                            'price': float(price) if pd.notna(price) else 0,
                            'changePercent': float(change_percent) if pd.notna(change_percent) else 0,
                            'volume': float(volume) if pd.notna(volume) else 0,
                            'turnover': float(turnover) if pd.notna(turnover) else 0
                        })
                    except Exception as e:
                        print(f"[{datetime.now()}] ⚠️ 解析人气榜数据行失败 (行{idx}): {str(e)[:100]}")
                        continue
                
                print(f"[{datetime.now()}] ✅ 成功解析 {len(hot_rank_list)} 条人气榜数据")
            else:
                print(f"[{datetime.now()}] ⚠️ 无法获取人气榜数据")
                
        except Exception as e:
            error_type = type(e).__name__
            error_msg = str(e)
            print(f"[{datetime.now()}] ⚠️ [人气榜接口] 获取人气榜数据异常: {error_type}")
            print(f"  错误消息: {error_msg[:300]}")
            try:
                import traceback
                print(f"  完整堆栈: {traceback.format_exc()[:500]}")
            except:
                pass
        
        # 恢复原始代理设置
        if original_http_proxy:
            os.environ['HTTP_PROXY'] = original_http_proxy
        if original_https_proxy:
            os.environ['HTTPS_PROXY'] = original_https_proxy
        if original_http_proxy_lower:
            os.environ['http_proxy'] = original_http_proxy_lower
        if original_https_proxy_lower:
            os.environ['https_proxy'] = original_https_proxy_lower
        
        # 构建返回结果
        result = {
            'hotRankList': hot_rank_list,
            'count': len(hot_rank_list),
            'updateTime': datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
            'source': 'AKShare - stock_hot_rank_latest_em'
        }
        
        if len(hot_rank_list) == 0:
            print(f"[{datetime.now()}] ⚠️ 未获取到人气榜数据")
            return jsonify({
                'success': True,
                'data': result,
                'message': '无法获取个股人气榜数据'
            })
        
        print(f"[{datetime.now()}] ✅ 成功获取个股人气榜数据 - 共{len(hot_rank_list)}条")
        return jsonify({'success': True, 'data': result})
        
    except Exception as e:
        error_msg = str(e)
        try:
            import traceback
            error_trace = traceback.format_exc()
            print(f"[{datetime.now()}] ❌ 获取个股人气榜数据失败: {error_msg}")
            print(error_trace)
        except:
            print(f"[{datetime.now()}] ❌ 获取个股人气榜数据失败: {error_msg}")
        return jsonify({
                'success': False,
                'error': error_msg,
                'message': '无法获取个股人气榜数据'
            }), 500

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
    print("  GET  /api/stock/industry/<stock_code> - 获取股票行业详情")
    print("  GET  /api/stock/hot-rank - 获取个股人气榜最新排名")
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

