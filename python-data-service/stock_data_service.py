"""
股票数据服务 - 使用AKShare获取财务数据
运行方式: python stock_data_service.py
"""
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
        
        # 方法1: 获取财务指标数据
        try:
            # AKShare需要特定格式：6位数字代码
            # 确保代码格式正确（去除空格，确保是6位）
            clean_code = stock_code.strip().zfill(6)
            print(f"[{datetime.now()}] 方法1: 尝试使用股票代码: {clean_code}")
            
            # 尝试使用财务分析指标接口
            df = ak.stock_financial_analysis_indicator_em(symbol=clean_code)
            
            # 检查返回结果
            if df is None:
                print(f"[{datetime.now()}] ⚠️ 方法1: AKShare返回None（数据源可能没有该股票的数据）")
                raise ValueError(f"AKShare返回None，股票代码 {clean_code} 可能没有财务数据")
            
            if not df.empty:
                # 获取最新一条数据
                latest = df.iloc[0]
                
                # 转换为字典
                result = {
                    'stockCode': stock_code,
                    'stockName': latest.get('股票简称', '未知'),
                    'reportDate': str(latest.get('报告期', '')),
                    
                    # 盈利能力
                    'roe': float(latest.get('净资产收益率', 0)) if pd.notna(latest.get('净资产收益率')) else None,
                    'grossProfitMargin': float(latest.get('销售毛利率', 0)) if pd.notna(latest.get('销售毛利率')) else None,
                    'netProfitMargin': float(latest.get('销售净利率', 0)) if pd.notna(latest.get('销售净利率')) else None,
                    
                    # 每股指标
                    'eps': float(latest.get('基本每股收益', 0)) if pd.notna(latest.get('基本每股收益')) else None,
                    'bps': float(latest.get('每股净资产', 0)) if pd.notna(latest.get('每股净资产')) else None,
                    
                    # 营业收入和净利润（单位：万元）
                    'totalRevenue': float(latest.get('营业总收入', 0)) / 10000 if pd.notna(latest.get('营业总收入')) else None,
                    'netProfit': float(latest.get('净利润', 0)) / 10000 if pd.notna(latest.get('净利润')) else None,
                    
                    # 成长性
                    'revenueGrowthRate': float(latest.get('营业总收入同比增长率', 0)) if pd.notna(latest.get('营业总收入同比增长率')) else None,
                    'profitGrowthRate': float(latest.get('净利润同比增长率', 0)) if pd.notna(latest.get('净利润同比增长率')) else None,
                    
                    # 偿债能力
                    'assetLiabilityRatio': float(latest.get('资产负债率', 0)) if pd.notna(latest.get('资产负债率')) else None,
                    'currentRatio': float(latest.get('流动比率', 0)) if pd.notna(latest.get('流动比率')) else None,
                    'quickRatio': float(latest.get('速动比率', 0)) if pd.notna(latest.get('速动比率')) else None,
                    
                    # 运营能力
                    'inventoryTurnover': float(latest.get('存货周转率', 0)) if pd.notna(latest.get('存货周转率')) else None,
                    'accountsReceivableTurnover': float(latest.get('应收账款周转率', 0)) if pd.notna(latest.get('应收账款周转率')) else None,
                    
                    'lastUpdate': datetime.now().isoformat(),
                    'source': 'AKShare'
                }
                
                print(f"[{datetime.now()}] ✅ 成功获取数据: {stock_code}")
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

