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
from bs4 import BeautifulSoup
import io
import base64

try:
    import matplotlib  # type: ignore
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt  # type: ignore
    import matplotlib.dates as mdates  # type: ignore
    from matplotlib.ticker import MaxNLocator  # type: ignore
    from matplotlib import font_manager  # type: ignore
    MATPLOTLIB_AVAILABLE = True
except Exception as matplotlib_import_error:
    matplotlib = None
    plt = None
    mdates = None
    MaxNLocator = None
    MATPLOTLIB_AVAILABLE = False
    print(f"[{datetime.now()}] ⚠️ 未能导入matplotlib，图表生成功能不可用: {matplotlib_import_error}")
else:
    try:
        font_candidates = ["Microsoft YaHei", "SimHei", "STHeiti", "Heiti TC", "Arial Unicode MS"]
        selected_font = None
        for font_name in font_candidates:
            try:
                font_manager.findfont(font_name, fallback_to_default=False)  # type: ignore[attr-defined]
                selected_font = font_name
                break
            except Exception:
                continue
        if selected_font:
            plt.rcParams["font.family"] = selected_font
        plt.rcParams["axes.unicode_minus"] = False
    except Exception as font_config_error:
        print(f"[{datetime.now()}] ⚠️ 配置matplotlib中文字体失败，将使用默认字体: {font_config_error}")

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

# 配置AKShare使用无代理环境（更彻底的代理禁用 - 使用monkey patch）
try:
    import requests
    from requests.adapters import HTTPAdapter
    from urllib3.util import Retry
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
except Exception as e:
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
                        time_str = ''
                        if pd.notna(time_val):
                            if isinstance(time_val, str):
                                try:
                                    # 尝试将字符串转换为datetime
                                    from datetime import datetime as dt
                                    time_val = pd.to_datetime(time_val)
                                except Exception:
                                    # 如果转换失败，使用原始字符串
                                    time_str = str(time_val)
                            if not time_str:
                                if hasattr(time_val, 'strftime'):
                                    time_str = time_val.strftime("%Y-%m-%d %H:%M:%S")
                                else:
                                    time_str = str(time_val)
                        
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

def _normalize_stock_code(stock_code: str) -> str:
    """标准化股票代码为6位数字，兼容带市场前缀的写法"""
    if not stock_code:
        return ""
    clean_code = stock_code.strip().upper()
    if clean_code.startswith("SH") or clean_code.startswith("SZ"):
        clean_code = clean_code[2:]
    return clean_code.zfill(6)


def _fetch_news_article_content(url: str) -> str:
    """根据新闻链接抓取正文内容，返回纯文本"""
    if not url:
        return ""

    try:
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                           "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
            "Accept-Language": "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7",
            "Connection": "keep-alive",
        }

        response = requests.get(url, headers=headers, timeout=15, verify=False)
        if response.status_code != 200:
            print(f"[{datetime.now()}] ⚠️ 抓取新闻正文失败，状态码: {response.status_code}, URL: {url}")
            return ""

        # 尝试使用页面自带编码，否则回退到apparent_encoding
        if not response.encoding or response.encoding.lower() in ("utf-8", "utf8", "" ):
            response.encoding = response.apparent_encoding or "utf-8"

        soup = BeautifulSoup(response.text, "html.parser")

        # 常见的正文容器选择器
        selectors = [
            "div#ContentBody",
            "div#artibody",
            "div.article-content",
            "div.article-body",
            "div#newsContent",
            "div.article-infor",
            "div.txtinfos",
        ]

        text_content = ""
        for selector in selectors:
            node = soup.select_one(selector)
            if node:
                paragraphs = [p.get_text(strip=True) for p in node.find_all(['p', 'div', 'span']) if p.get_text(strip=True)]
                text_content = "\n".join(paragraphs)
                if text_content:
                    break

        if not text_content:
            paragraphs = [p.get_text(strip=True) for p in soup.find_all('p') if p.get_text(strip=True)]
            text_content = "\n".join(paragraphs)

        if not text_content:
            raw_text = soup.get_text(separator='\n')
            lines = [line.strip() for line in raw_text.splitlines() if line.strip()]
            text_content = "\n".join(lines[:200])

        return text_content.strip()
    except Exception as e:
        print(f"[{datetime.now()}] ⚠️ 抓取新闻正文发生异常: {e}. URL: {url}")
        return ""


@app.route('/api/news/stock/<stock_code>', methods=['GET'])
def get_stock_news(stock_code):
    """获取指定股票的最新新闻（基于AKShare stock_news_em）"""
    start_time = time.time()
    try:
        clean_code = _normalize_stock_code(stock_code)
        if not clean_code:
            return jsonify({'success': True, 'data': {'stockCode': stock_code, 'items': []}})

        print(f"[{datetime.now()}] 获取个股新闻: 输入={stock_code}, 标准化={clean_code}")

        df_news = ak.stock_news_em(symbol=clean_code)

        if df_news is None or df_news.empty:
            print(f"[{datetime.now()}] ⚠️ AKShare stock_news_em 返回空数据: {clean_code}")
            return jsonify({'success': True, 'data': {'stockCode': stock_code, 'items': []}})

        df_news = df_news.copy()

        publish_col = '发布时间'
        if publish_col in df_news.columns:
            df_news[publish_col] = pd.to_datetime(df_news[publish_col], errors='coerce')
            df_news = df_news.sort_values(by=publish_col, ascending=False)
        else:
            df_news = df_news.sort_values(by=df_news.columns[0], ascending=False)

        top_news = df_news.head(10)

        items = []
        for _, row in top_news.iterrows():
            keywords = str(row.get('关键词', '') or '').strip()
            title = str(row.get('新闻标题', '') or '').strip()
            summary = str(row.get('新闻内容', '') or '').strip()
            publish_time = row.get(publish_col) if publish_col in row else None
            source = str(row.get('文章来源', '') or '').strip()
            url = str(row.get('新闻链接', '') or '').strip()

            if isinstance(publish_time, pd.Timestamp) and not pd.isna(publish_time):
                publish_time_str = publish_time.to_pydatetime().isoformat()
            else:
                publish_time_str = str(publish_time) if publish_time is not None else ''

            full_content = _fetch_news_article_content(url) if url else ''

            items.append({
                'keywords': keywords,
                'title': title,
                'summary': summary,
                'publishTime': publish_time_str,
                'source': source,
                'url': url,
                'content': full_content or summary or title,
            })
        elapsed = time.time() - start_time
        print(f"[{datetime.now()}] ✅ 成功获取个股新闻 {len(items)} 条，用时 {elapsed:.2f}s")

        return jsonify({
            'success': True,
            'data': {
                'stockCode': stock_code,
                'normalizedCode': clean_code,
                'count': len(items),
                'fetchedAt': datetime.now().isoformat(),
                'items': items
            }
        })
    except Exception as e:
        error_msg = str(e)
        print(f"[{datetime.now()}] ❌ 获取个股新闻失败: {error_msg}")
        print(traceback.format_exc())
        return jsonify({
            'success': False,
            'error': error_msg,
            'trace': traceback.format_exc() if os.getenv('FLASK_ENV') == 'development' else None
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

def _normalize_stock_identifier(stock_code: str):
    clean_code = stock_code.strip().zfill(6)
    symbol = f"sh{clean_code}" if clean_code.startswith('6') else f"sz{clean_code}"
    return clean_code, symbol


def _filter_dataframe_by_date_range(df: pd.DataFrame, start_date: datetime, end_date: datetime):
    if df is None or df.empty:
        return None, None

    df = df.copy()
    date_column = None
    for col in ['日期', 'date', 'Date', '交易日期']:
        if col in df.columns:
            df[col] = pd.to_datetime(df[col], errors='coerce')
            df = df[df[col].notna()]
            date_column = col
            break

    if date_column is not None:
        df = df[(df[date_column] >= start_date) & (df[date_column] <= end_date)]
        if df.empty:
            return None, date_column

    return df, date_column


def _fetch_history_dataframe_with_fallback(stock_code: str, months: int, allow_extended: bool = True):
    clean_code, symbol = _normalize_stock_identifier(stock_code)
    end_date = datetime.now()
    target_start_date = end_date - timedelta(days=months * 30)

    adjust_options = ['qfq', 'hfq', '']
    months_candidates = [months]
    if allow_extended and months < 6:
        months_candidates.append(6)
    months_candidates = [m for m in months_candidates if m > 0]

    def log_attempt(source, months_span, adjust_value):
        adjust_label = adjust_value if adjust_value else '无复权'
        print(f"[{datetime.now()}] 尝试{source}: {symbol}, 月数: {months_span}, 复权: {adjust_label}")

    # 优先尝试 stock_zh_a_daily（一次性获取全量日线数据，再按时间过滤）
    try:
        print(f"[{datetime.now()}] 尝试 stock_zh_a_daily: {symbol}")
        df_candidate = ak.stock_zh_a_daily(symbol=symbol)
        if df_candidate is not None and not df_candidate.empty:
            df_filtered, _ = _filter_dataframe_by_date_range(df_candidate, target_start_date, end_date)
            if df_filtered is not None and not df_filtered.empty:
                return df_filtered, "stock_zh_a_daily", target_start_date, end_date
            else:
                print(f"[{datetime.now()}] stock_zh_a_daily 返回数据，但过滤后为空")
        else:
            print(f"[{datetime.now()}] stock_zh_a_daily 返回空数据")
    except Exception as exc:
        print(f"[{datetime.now()}] ⚠️ stock_zh_a_daily 调用失败: {str(exc)}")
        print(traceback.format_exc()[:500])

    # 如果日线接口不可用或无数据，回退到 stock_zh_a_hist，涵盖前复权和后复权
    for months_span in months_candidates:
        attempt_start = end_date - timedelta(days=months_span * 30)
        for adjust in adjust_options:
            log_attempt("stock_zh_a_hist", months_span, adjust)
            try:
                df_candidate = ak.stock_zh_a_hist(
                    symbol=symbol,
                    period="daily",
                    start_date=attempt_start.strftime("%Y%m%d"),
                    end_date=end_date.strftime("%Y%m%d"),
                    adjust=adjust or ""
                )
                if df_candidate is not None and not df_candidate.empty:
                    df_filtered, date_col = _filter_dataframe_by_date_range(df_candidate, target_start_date, end_date)
                    if df_filtered is not None and not df_filtered.empty:
                        method = f"stock_zh_a_hist ({months_span}个月, {adjust or '无复权'})"
                        if months_span != months:
                            method += " -> 过滤至目标区间"
                        return df_filtered, method, target_start_date, end_date
                    else:
                        print(f"[{datetime.now()}] stock_zh_a_hist 返回数据，但过滤后为空 (复权: {adjust or '无复权'})")
            except Exception as exc:
                print(f"[{datetime.now()}] ⚠️ stock_zh_a_hist 调用失败: {str(exc)}")
                print(traceback.format_exc()[:500])

    # 尝试腾讯历史数据接口
    months_for_tx = months_candidates if months_candidates else [months]
    for months_span in months_for_tx:
        attempt_start = end_date - timedelta(days=months_span * 30)
        try:
            print(f"[{datetime.now()}] 尝试 stock_zh_a_hist_tx: {symbol}, 月数: {months_span}")
            df_candidate = ak.stock_zh_a_hist_tx(
                symbol=symbol,
                start_date=attempt_start.strftime("%Y%m%d"),
                end_date=end_date.strftime("%Y%m%d")
            )
            if df_candidate is not None and not df_candidate.empty:
                df_filtered, _ = _filter_dataframe_by_date_range(df_candidate, target_start_date, end_date)
                if df_filtered is not None and not df_filtered.empty:
                    method = f"stock_zh_a_hist_tx ({months_span}个月)"
                    if months_span != months:
                        method += " -> 过滤至目标区间"
                    return df_filtered, method, target_start_date, end_date
                else:
                    print(f"[{datetime.now()}] stock_zh_a_hist_tx 过滤后为空")
        except Exception as exc:
            print(f"[{datetime.now()}] ⚠️ stock_zh_a_hist_tx 调用失败: {str(exc)}")
            print(traceback.format_exc()[:500])

    raise ValueError(f"所有AKShare方法都失败，无法获取股票 {clean_code} 的历史数据")


def _convert_history_df_to_records(df: pd.DataFrame):
    records = []
    if df is None or df.empty:
        return records

    for _, row in df.iterrows():
        date_val = None
        for col_name in ['日期', 'date', 'Date', '交易日期']:
            if col_name in row.index and pd.notna(row[col_name]):
                value = row[col_name]
                if isinstance(value, str):
                    date_val = value
                elif hasattr(value, 'strftime'):
                    date_val = value.strftime("%Y-%m-%d")
                else:
                    date_val = str(value)
                break

        if not date_val:
            continue

        def extract_float(columns, default=0.0):
            for col in columns:
                if col in row.index and pd.notna(row[col]):
                    try:
                        return float(row[col])
                    except Exception:
                        continue
            return default

        open_val = extract_float(['开盘', 'open', 'Open', '开盘价'])
        close_val = extract_float(['收盘', 'close', 'Close', '收盘价'])
        high_val = extract_float(['最高', 'high', 'High', '最高价'], default=close_val if close_val > 0 else 0.0)
        low_val = extract_float(['最低', 'low', 'Low', '最低价'], default=close_val if close_val > 0 else 0.0)
        volume_val = extract_float(['成交量', 'volume', 'Volume'])
        turnover_val = extract_float(['成交额', 'amount', 'Amount', '成交金额', 'turnover'])

        if close_val <= 0:
            continue

        records.append({
            'tradeDate': date_val,
            'open': open_val,
            'close': close_val,
            'high': high_val if high_val > 0 else close_val,
            'low': low_val if low_val > 0 else close_val,
            'volume': volume_val,
            'turnover': turnover_val
        })

    return records


def _build_history_payload(stock_code: str, months: int, allow_extended: bool = True):
    df, method_used, start_date, end_date = _fetch_history_dataframe_with_fallback(stock_code, months, allow_extended)
    records = _convert_history_df_to_records(df)

    if not records:
        raise ValueError("数据转换失败，无法解析AKShare返回的数据格式")

    return {
        'stockCode': stock_code,
        'startDate': start_date.strftime("%Y-%m-%d"),
        'endDate': end_date.strftime("%Y-%m-%d"),
        'totalRecords': len(records),
        'method': method_used,
        'data': records
    }


def _safe_to_datetime(value):
    if isinstance(value, pd.Timestamp):
        return value.to_pydatetime()
    if isinstance(value, datetime):
        return value
    try:
        parsed = pd.to_datetime(value)
        if isinstance(parsed, pd.Timestamp):
            return parsed.to_pydatetime()
        return parsed
    except Exception:
        return None


def _format_date_str(value):
    dt_value = _safe_to_datetime(value)
    if dt_value is None:
        return None
    return dt_value.strftime("%Y-%m-%d")


def _generate_price_chart(df: pd.DataFrame):
    """
    生成股价走势图并标注关键数据，返回Base64编码的PNG图像和关键信息。
    """
    try:
        if not MATPLOTLIB_AVAILABLE or plt is None or mdates is None or MaxNLocator is None:
            return None, None

        if df is None or df.empty or len(df) < 2:
            return None, None

        plot_df = df.copy()
        plot_df['tradeDate'] = pd.to_datetime(plot_df['tradeDate'])
        plot_df = plot_df.dropna(subset=['tradeDate', 'close'])

        if plot_df.empty:
            return None, None

        fig, ax = plt.subplots(figsize=(10, 4.5))

        # 绘制收盘价
        ax.plot(plot_df['tradeDate'], plot_df['close'], label='收盘价', color='#1f77b4', linewidth=2)

        # 绘制移动平均线
        ma_colors = {
            'MA5': '#ff7f0e',
            'MA10': '#2ca02c',
            'MA20': '#9467bd',
            'MA60': '#8c564b'
        }
        for ma_key, color in ma_colors.items():
            if ma_key in plot_df.columns and plot_df[ma_key].notna().any():
                ax.plot(plot_df['tradeDate'], plot_df[ma_key], label=ma_key, linewidth=1.5, alpha=0.85, color=color)

        # 关键点
        highlights = {}

        def _add_point(row, label, color, offset):
            trade_dt = _safe_to_datetime(row['tradeDate'])
            price = float(row.get('close') if label == '当前价' else row.get('high') if '高' in label else row.get('low'))
            if trade_dt is None or price is None:
                return
            ax.scatter(trade_dt, price, color=color, s=55, zorder=5)
            annotation = f"{label} {price:.2f}"
            ax.annotate(
                annotation,
                xy=(trade_dt, price),
                xytext=offset,
                textcoords='offset points',
                fontsize=9,
                fontweight='bold',
                color=color,
                arrowprops=dict(arrowstyle="->", color=color, lw=1.1, alpha=0.8),
                bbox=dict(boxstyle="round,pad=0.2", fc="white", ec=color, lw=0.8, alpha=0.8)
            )

        # 最高价
        if 'high' in plot_df.columns and plot_df['high'].notna().any():
            high_idx = plot_df['high'].idxmax()
            high_row = plot_df.loc[high_idx]
            _add_point(high_row, '最高价', '#d62728', (10, 25))
            highlights['highest'] = {
                'date': _format_date_str(high_row['tradeDate']),
                'price': float(high_row['high'])
            }

        # 最低价
        if 'low' in plot_df.columns and plot_df['low'].notna().any():
            low_idx = plot_df['low'].idxmin()
            low_row = plot_df.loc[low_idx]
            _add_point(low_row, '最低价', '#17becf', (10, -35))
            highlights['lowest'] = {
                'date': _format_date_str(low_row['tradeDate']),
                'price': float(low_row['low'])
            }

        # 当前价（最新收盘价）
        latest_row = plot_df.iloc[-1]
        _add_point(latest_row, '当前价', '#2ca02c', (-80, -25))
        highlights['latest'] = {
            'date': _format_date_str(latest_row['tradeDate']),
            'price': float(latest_row['close'])
        }

        # 记录移动平均线值
        ma_values = {}
        for ma_key in ['MA5', 'MA10', 'MA20', 'MA60']:
            if ma_key in plot_df.columns:
                ma_val = plot_df[ma_key].iloc[-1]
                if pd.notna(ma_val):
                    ma_values[ma_key] = float(ma_val)
        if ma_values:
            highlights['movingAverages'] = ma_values

        # 图形美化
        ax.set_title('股价走势（含主要均线）', fontsize=12, fontweight='bold')
        ax.set_xlabel('日期')
        ax.set_ylabel('价格（元）')
        ax.xaxis.set_major_locator(mdates.AutoDateLocator(maxticks=8))
        ax.xaxis.set_major_formatter(mdates.DateFormatter('%m-%d'))
        fig.autofmt_xdate()
        ax.yaxis.set_major_locator(MaxNLocator(nbins=6, prune='both'))
        ax.grid(True, linestyle='--', alpha=0.3)
        ax.legend(loc='upper left', ncol=2, fontsize=9)
        ax.set_xlim(plot_df['tradeDate'].iloc[0], plot_df['tradeDate'].iloc[-1])

        # 收益区间
        start_price = float(plot_df['close'].iloc[0])
        end_price = float(plot_df['close'].iloc[-1])
        change_pct = (end_price - start_price) / start_price * 100 if start_price != 0 else 0
        highlights['period'] = {
            'startDate': _format_date_str(plot_df['tradeDate'].iloc[0]),
            'endDate': _format_date_str(plot_df['tradeDate'].iloc[-1]),
            'startPrice': start_price,
            'endPrice': end_price,
            'changePercent': round(change_pct, 2)
        }

        buf = io.BytesIO()
        fig.tight_layout()
        fig.savefig(buf, format='png', dpi=150, bbox_inches='tight')
        plt.close(fig)
        buf.seek(0)
        img_base64 = base64.b64encode(buf.read()).decode('utf-8')
        buf.close()

        return img_base64, highlights
    except Exception as chart_error:
        print(f"[{datetime.now()}] ⚠️ 生成股价走势图失败: {chart_error}")
        return None, None


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
        
        # 先获取历史数据（复用 /history 逻辑，支持多种复权和时间范围尝试）
        try:
            history_payload = _build_history_payload(stock_code, months, allow_extended=True)
            history_records = history_payload['data']
            method_used = history_payload.get('method', '未知')

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

        chart_image, chart_highlights = _generate_price_chart(
            df[['tradeDate', 'close', 'high', 'low', 'MA5', 'MA10', 'MA20', 'MA60']].copy()
        )
        if chart_image:
            analysis_result['chart'] = {
                'imageBase64': chart_image,
                'contentType': 'image/png',
                'highlights': chart_highlights
            }
        
        # 7. 生成洞察
        insights = []
        if chart_image:
            insights.append("已生成股价走势图，标注最高、最低与当前价等关键点位供参考。")
        
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
        
        # 使用 stock_board_industry_spot_em 根据行业名称获取行业板块实时行情
        industry_stocks = []
        industry_performance = {}
        industry_trends = ''
        industry_market_data = {}  # 行业板块市场数据（必须在此初始化，避免后续使用时变量未定义错误）
        
        # 如果获取到了行业名称，使用 stock_board_industry_spot_em 获取实时行情
        if industry_name and industry_name != '未知':
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
                import urllib3
                urllib3.disable_warnings()
                
                # 使用 stock_board_industry_spot_em 获取行业板块实时行情（带重试）
                df_industry_spot = None
                for attempt in range(3):
                    try:
                        # 每次重试前增加延迟，避免请求过快
                        if attempt > 0:
                            delay = 1.0 * attempt  # 第2次重试延迟1秒，第3次延迟2秒
                            print(f"[{datetime.now()}] ⏳ [行业接口] 等待{delay:.1f}秒后重试...")
                            time.sleep(delay)
                        
                        print(f"[{datetime.now()}] 📡 [行业接口] 尝试调用 stock_board_industry_spot_em(symbol='{industry_name}') (尝试 {attempt + 1}/3)...")
                        start_time = time.time()
                        
                        # 调用AKShare接口获取行业板块实时行情
                        df_industry_spot = ak.stock_board_industry_spot_em(symbol=industry_name)
                        elapsed_time = time.time() - start_time
                        
                        if df_industry_spot is not None and not df_industry_spot.empty:
                            print(f"[{datetime.now()}] ✅ [行业接口] 成功获取行业板块实时行情，耗时: {elapsed_time:.2f}秒")
                            break
                        else:
                            print(f"[{datetime.now()}] ⚠️ [行业接口] 返回数据为空")
                            time.sleep(0.5)
                    except Exception as e:
                        error_type = type(e).__name__
                        error_msg = str(e)
                        elapsed_time = time.time() - start_time if 'start_time' in locals() else 0
                        
                        print(f"[{datetime.now()}] ❌ [行业接口] 获取行业板块实时行情失败 (尝试 {attempt + 1}/3)")
                        print(f"    错误类型: {error_type}")
                        print(f"    错误消息: {error_msg[:200]}")
                        print(f"    耗时: {elapsed_time:.2f}秒")
                        
                        if attempt < 2:
                            time.sleep(1)
                        else:
                            print(f"[{datetime.now()}] ❌ [行业接口] 获取行业板块实时行情最终失败")
                            df_industry_spot = None
                            break
                
                # 如果成功获取到行业板块实时行情数据，解析数据
                if df_industry_spot is not None and not df_industry_spot.empty:
                    try:
                        # stock_board_industry_spot_em 返回的是行业板块的实时行情数据
                        # 通常包含：板块名称、板块代码、最新价、涨跌幅、涨跌额、总市值、换手率、上涨家数、下跌家数、领涨股票等信息
                        
                        # 提取行业板块代码（如果存在）
                        if '板块代码' in df_industry_spot.columns:
                            industry_code = str(df_industry_spot.iloc[0].get('板块代码', '')).strip()
                        
                        # 提取行业板块的实时行情数据
                        matched_row = df_industry_spot.iloc[0]
                        
                        # 提取行业板块的市场数据
                        latest_price = matched_row.get('最新价', matched_row.get('现价', None))
                        change_amount = matched_row.get('涨跌额', None)
                        change_percent = matched_row.get('涨跌幅', None)
                        total_market_cap = matched_row.get('总市值', None)
                        turnover_rate = matched_row.get('换手率', None)
                        rising_count = matched_row.get('上涨家数', None)
                        falling_count = matched_row.get('下跌家数', None)
                        leader_stock = matched_row.get('领涨股票', matched_row.get('领涨股', None))
                        leader_change_percent = matched_row.get('领涨股票-涨跌幅', matched_row.get('领涨股涨跌幅', None))
                        
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
                        
                        print(f"[{datetime.now()}] ✅ 成功提取行业板块实时行情数据: {industry_name}")
                        
                        # 如果获取到了行业代码，可以获取行业成分股
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
                                    
                                    # 计算行业平均表现指标
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
                        print(f"[{datetime.now()}] ⚠️ 解析行业板块实时行情数据失败: {str(e)}")
                else:
                    # 如果 stock_board_industry_spot_em 获取不到数据，回退到使用 stock_board_industry_name_em
                    print(f"[{datetime.now()}] ⚠️ 无法获取行业板块实时行情数据，回退到使用 stock_board_industry_name_em")
                    
                    try:
                        # 获取所有行业板块列表（带重试）
                        df_industry_board = None
                        for attempt in range(3):
                            try:
                                if attempt > 0:
                                    delay = 1.0 * attempt
                                    print(f"[{datetime.now()}] ⏳ [行业接口-回退] 等待{delay:.1f}秒后重试...")
                                    time.sleep(delay)
                                
                                print(f"[{datetime.now()}] 📡 [行业接口-回退] 尝试调用 stock_board_industry_name_em() (尝试 {attempt + 1}/3)...")
                                start_time = time.time()
                                
                                df_industry_board = ak.stock_board_industry_name_em()
                                elapsed_time = time.time() - start_time
                                
                                if df_industry_board is not None and not df_industry_board.empty:
                                    print(f"[{datetime.now()}] ✅ [行业接口-回退] 成功获取行业板块列表，耗时: {elapsed_time:.2f}秒，共{len(df_industry_board)}个行业")
                                    break
                                else:
                                    print(f"[{datetime.now()}] ⚠️ [行业接口-回退] 返回数据为空")
                                    time.sleep(0.5)
                            except Exception as e:
                                error_type = type(e).__name__
                                error_msg = str(e)
                                elapsed_time = time.time() - start_time if 'start_time' in locals() else 0
                                
                                print(f"[{datetime.now()}] ❌ [行业接口-回退] 获取行业板块列表失败 (尝试 {attempt + 1}/3)")
                                print(f"    错误类型: {error_type}")
                                print(f"    错误消息: {error_msg[:200]}")
                                print(f"    耗时: {elapsed_time:.2f}秒")
                                
                                if attempt < 2:
                                    time.sleep(1)
                                else:
                                    print(f"[{datetime.now()}] ❌ [行业接口-回退] 获取行业板块列表最终失败")
                                    df_industry_board = None
                                    break
                        
                        # 如果成功获取到行业板块列表，查找匹配的行业
                        if df_industry_board is not None and not df_industry_board.empty:
                            matched_industry = None
                            
                            # 先尝试精确匹配
                            if industry_name and industry_name != '未知':
                                matched_industry = df_industry_board[df_industry_board['板块名称'] == industry_name]
                            
                            # 如果精确匹配失败，尝试包含匹配
                            if (matched_industry is None or matched_industry.empty) and industry_name and industry_name != '未知':
                                matched_industry = df_industry_board[df_industry_board['板块名称'].str.contains(industry_name, na=False)]
                            
                            if matched_industry is not None and not matched_industry.empty:
                                matched_row = matched_industry.iloc[0]
                                industry_code = matched_row.get('板块代码', '')
                                if not industry_name or industry_name == '未知':
                                    industry_name = matched_row.get('板块名称', '未知')
                                
                                # 提取行业板块的市场数据
                                try:
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
                                    
                                    print(f"[{datetime.now()}] ✅ [行业接口-回退] 成功提取行业板块数据: {industry_name}")
                                except Exception as e:
                                    print(f"[{datetime.now()}] ⚠️ [行业接口-回退] 提取行业板块市场数据失败: {str(e)}")
                                
                                # 如果获取到了行业代码，可以获取行业成分股
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
                                                    print(f"[{datetime.now()}] ⚠️ [行业接口-回退] 获取行业成分股失败 (尝试 {retry + 1}/3): {str(e)[:80]}，重试中...")
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
                                            
                                            # 计算行业平均表现指标
                                            if len(industry_stocks) > 0:
                                                prices = [s['price'] for s in industry_stocks if s['price'] > 0]
                                                changes = [s['changePercent'] for s in industry_stocks if s['changePercent'] != 0]
                                                
                                                if prices and changes:
                                                    industry_performance = {
                                                        'avgPE': None,
                                                        'avgPB': None,
                                                        'avgROE': None,
                                                        'totalMarketCap': None,
                                                        'avgChangePercent': round(sum(changes) / len(changes), 2) if changes else 0,
                                                        'stockCount': len(industry_stocks),
                                                        'avgPrice': round(sum(prices) / len(prices), 2) if prices else 0
                                                    }
                                            
                                            print(f"[{datetime.now()}] ✅ [行业接口-回退] 成功获取行业成分股: {industry_name} ({industry_code})，共{len(industry_stocks)}只股票")
                                    except Exception as e:
                                        print(f"[{datetime.now()}] ⚠️ [行业接口-回退] 获取行业成分股失败: {str(e)}")
                            else:
                                print(f"[{datetime.now()}] ⚠️ [行业接口-回退] 未找到匹配的行业: {industry_name}")
                    except Exception as e:
                        error_type = type(e).__name__
                        error_msg = str(e)
                        print(f"[{datetime.now()}] ⚠️ [行业接口-回退] 回退逻辑执行异常: {error_type}")
                        print(f"  错误消息: {error_msg[:300]}")
                        try:
                            import traceback
                            print(f"  完整堆栈: {traceback.format_exc()[:500]}")
                        except:
                            pass
            except Exception as e:
                error_type = type(e).__name__
                error_msg = str(e)
                print(f"[{datetime.now()}] ⚠️ [行业接口] 获取行业板块实时行情异常: {error_type}")
                print(f"  错误消息: {error_msg[:300]}")
                try:
                    import traceback
                    print(f"  完整堆栈: {traceback.format_exc()[:500]}")
                except:
                    pass
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

@app.route('/api/stock/hot-rank/<stock_code>', methods=['GET'])
def get_hot_rank_by_code(stock_code):
    """根据股票代码获取最新人气排名"""
    try:
        request_time = datetime.now()
        print(f"[{request_time}] 请求个股人气榜数据: {stock_code}")

        if not stock_code:
            return jsonify({'success': False, 'error': 'stock_code_required'}), 400

        clean_code = stock_code.strip()
        normalized = clean_code.replace('-', '').replace('_', '').upper()

        # 拆分前缀和纯数字部分
        prefix = ''
        digits = ''.join(ch for ch in normalized if ch.isdigit())

        if normalized.startswith('SZ') or normalized.startswith('SH'):
            prefix = normalized[:2]
            if len(normalized) >= 2 and digits:
                digits = normalized[2:]
        elif len(digits) == 6:
            if digits.startswith('6'):
                prefix = 'SH'
            else:
                prefix = 'SZ'
        else:
            # 无法从传入的代码推断出有效的股票代码
            return jsonify({'success': False, 'error': 'invalid_stock_code', 'message': f'无法解析股票代码 {stock_code}'}), 400

        if len(digits) != 6 or not digits.isdigit():
            return jsonify({'success': False, 'error': 'invalid_stock_code', 'message': f'股票代码格式错误 {stock_code}'}), 400

        symbol = f"{prefix}{digits}"

        print(f"[{datetime.now()}] 解析股票代码成功: 输入={stock_code}, 规范化后={symbol}")

        df_hot_rank = ak.stock_hot_rank_latest_em(symbol=symbol)

        if df_hot_rank is None or df_hot_rank.empty:
            print(f"[{datetime.now()}] ⚠️ stock_hot_rank_latest_em 返回空数据: {symbol}")
            return jsonify({'success': True, 'data': None, 'message': '暂无人气排名数据'}), 200

        # 将DataFrame转换为字典
        data_map = {}
        for _, row in df_hot_rank.iterrows():
            item = str(row.get('item', '')).strip()
            value = row.get('value')

            if isinstance(value, (np.generic, np.ndarray)):
                try:
                    value = value.item()
                except Exception:
                    value = value.tolist() if hasattr(value, 'tolist') else value

            if item:
                data_map[item] = value

        def try_parse_int(val):
            try:
                if val is None or val == '':
                    return None
                return int(float(val))
            except Exception:
                return None

        response_data = {
            'stockCode': digits,
            'symbol': symbol,
            'marketType': data_map.get('marketType'),
            'marketAllCount': try_parse_int(data_map.get('marketAllCount')),
            'calcTime': data_map.get('calcTime'),
            'innerCode': data_map.get('innerCode'),
            'srcSecurityCode': data_map.get('srcSecurityCode'),
            'rank': try_parse_int(data_map.get('rank')),
            'rankChange': try_parse_int(data_map.get('rankChange')),
            'hisRankChange': try_parse_int(data_map.get('hisRankChange')),
            'hisRankChangeRank': try_parse_int(data_map.get('hisRankChange_rank')),
            'flag': try_parse_int(data_map.get('flag'))
        }

        print(f"[{datetime.now()}] ✅ 成功获取人气排名: {symbol} -> 排名 {response_data['rank']}")

        return jsonify({
            'success': True,
            'data': response_data,
            'source': 'AKShare - stock_hot_rank_latest_em'
        })

    except Exception as e:
        error_msg = str(e)
        print(f"[{datetime.now()}] ❌ 获取个股人气榜数据失败: {error_msg}")
        try:
            print(traceback.format_exc())
        except Exception:
            pass
        return jsonify({'success': False, 'error': error_msg}), 500


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
                # 打印列名以便调试（仅第一次）
                if len(hot_rank_list) == 0:
                    print(f"[{datetime.now()}] 📋 [人气榜接口] 数据列名: {list(df_hot_rank.columns)}")
                
                # 解析数据并构建返回格式
                # stock_hot_rank_latest_em返回的字段包括：rank（排名）、rankChange（排名变化）、hisRankChange（历史排名变化）等
                for idx, row in df_hot_rank.iterrows():
                    try:
                        # 提取股票代码和名称（用于匹配）
                        code = str(row.get('代码', row.get('股票代码', row.get('code', '')))).strip()
                        name = str(row.get('名称', row.get('股票名称', row.get('name', '')))).strip()
                        
                        # 提取排名相关字段（rank、rankChange、hisRankChange）
                        # rank: 当前排名（尝试多种可能的字段名）
                        rank = row.get('排名', row.get('rank', row.get('当前排名', None)))
                        if pd.isna(rank):
                            rank = None
                        
                        # rankChange: 排名变化（与上一期相比）
                        rank_change = row.get('排名变化', row.get('rankChange', row.get('排名变动', None)))
                        if pd.isna(rank_change):
                            rank_change = None
                        
                        # hisRankChange: 历史排名变化
                        his_rank_change = row.get('历史排名变化', row.get('hisRankChange', row.get('历史排名变动', None)))
                        if pd.isna(his_rank_change):
                            his_rank_change = None
                        
                        # 只返回rank、rankChange、hisRankChange这三个字段
                        if rank is not None:
                            hot_rank_list.append({
                                'rank': int(rank) if pd.notna(rank) else None,
                                'rankChange': int(rank_change) if pd.notna(rank_change) else None,
                                'hisRankChange': int(his_rank_change) if pd.notna(his_rank_change) else None,
                                'code': code,  # 保留code用于匹配股票
                                'name': name   # 保留name用于显示
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

