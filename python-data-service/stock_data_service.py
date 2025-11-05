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
import traceback
from datetime import datetime

app = Flask(__name__)
CORS(app)  # 允许跨域请求

@app.route('/health', methods=['GET'])
def health():
    """健康检查"""
    return jsonify({'status': 'ok', 'service': 'stock-data-service'})

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

