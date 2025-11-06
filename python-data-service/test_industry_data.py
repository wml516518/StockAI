"""
测试行业数据获取功能
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

import requests
import json
from datetime import datetime

def test_industry_api(stock_code):
    """
    测试行业接口
    """
    print(f"\n{'='*60}")
    print(f"测试股票代码: {stock_code}")
    print(f"{'='*60}\n")
    
    # Python服务地址
    python_service_url = os.getenv("PYTHON_DATA_SERVICE_URL", "http://localhost:5001")
    
    # 测试接口
    url = f"{python_service_url}/api/stock/industry/{stock_code}"
    
    print(f"请求URL: {url}")
    print(f"时间: {datetime.now()}\n")
    
    try:
        # 发送请求
        response = requests.get(url, timeout=60)
        
        print(f"响应状态码: {response.status_code}")
        print(f"响应头: {dict(response.headers)}\n")
        
        if response.status_code == 200:
            data = response.json()
            
            print("✅ 请求成功！")
            print("\n返回数据:")
            print(json.dumps(data, indent=2, ensure_ascii=False))
            
            # 解析数据
            if data.get('success'):
                result_data = data.get('data', {})
                
                print(f"\n{'='*60}")
                print("数据解析:")
                print(f"{'='*60}")
                print(f"股票代码: {result_data.get('stockCode', 'N/A')}")
                print(f"行业名称: {result_data.get('industryName', 'N/A')}")
                industry_code = result_data.get('industryCode', '')
                print(f"行业代码: {industry_code if industry_code else '❌ 为空（可能未找到匹配的行业板块）'}")
                print(f"行业描述: {result_data.get('description', 'N/A')}")
                
                # 行业表现数据
                performance = result_data.get('performance', {})
                if performance and any(performance.values()):  # 检查是否有非None值
                    print(f"\n行业表现指标:")
                    print(f"  - 平均涨跌幅: {performance.get('avgChangePercent', 'N/A')}%")
                    print(f"  - 股票数量: {performance.get('stockCount', 'N/A')}")
                    print(f"  - 平均价格: {performance.get('avgPrice', 'N/A')}")
                else:
                    print(f"\n行业表现指标: ❌ 无数据（可能未获取到行业成分股）")
                
                # 行业内股票列表
                stocks = result_data.get('stocks', [])
                print(f"\n行业内股票列表（共{len(stocks)}只）:")
                if stocks:
                    for i, stock in enumerate(stocks[:10], 1):  # 只显示前10只
                        print(f"  {i}. {stock.get('name', 'N/A')} ({stock.get('code', 'N/A')}) "
                              f"- 价格: {stock.get('price', 'N/A')}元 "
                              f"- 涨跌幅: {stock.get('changePercent', 'N/A')}%")
                    if len(stocks) > 10:
                        print(f"  ... 还有 {len(stocks) - 10} 只股票未显示")
                else:
                    print("  ❌ （无股票数据 - 可能原因：1) 未找到行业代码 2) 获取成分股失败 3) 网络问题）")
                
                print(f"\n数据来源: {result_data.get('source', 'N/A')}")
                print(f"更新时间: {result_data.get('lastUpdate', 'N/A')}")
                
                return True
            else:
                print(f"\n❌ 请求失败: {data.get('error', '未知错误')}")
                print(f"错误信息: {data.get('message', 'N/A')}")
                return False
        else:
            print(f"❌ HTTP错误: {response.status_code}")
            print(f"响应内容: {response.text}")
            return False
            
    except requests.exceptions.ConnectionError as e:
        print(f"❌ 连接错误: 无法连接到Python服务 ({python_service_url})")
        print(f"错误信息: {str(e)}")
        print("\n提示: 请确保Python服务正在运行!")
        return False
    except requests.exceptions.Timeout as e:
        print(f"❌ 请求超时: {str(e)}")
        return False
    except Exception as e:
        print(f"❌ 发生错误: {str(e)}")
        import traceback
        traceback.print_exc()
        return False

def test_direct_akshare(stock_code):
    """
    直接测试AKShare获取行业数据（需要在无代理环境下运行，或配置正确的代理）
    """
    print(f"\n{'='*60}")
    print(f"直接测试AKShare - 股票代码: {stock_code}")
    print(f"{'='*60}\n")
    
    try:
        import akshare as ak
        
        # 禁用代理（如果存在）
        import os
        original_proxies = {}
        for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
            original_proxies[proxy_var] = os.environ.get(proxy_var)
            os.environ.pop(proxy_var, None)
        
        print("✅ 已临时禁用代理设置\n")
        
        clean_code = stock_code.strip().zfill(6)
        print(f"处理后的股票代码: {clean_code}")
        
        # 1. 获取股票基本信息
        print("\n[步骤1] 获取股票基本信息...")
        try:
            df_info = ak.stock_individual_info_em(symbol=clean_code)
            if df_info is not None and not df_info.empty:
                print(f"✅ 成功获取股票基本信息（共{len(df_info)}行）")
                
                # 查找行业字段
                industry_fields = ['所属行业', '行业', '行业分类', '板块']
                industry_name = None
                for field in industry_fields:
                    industry_row = df_info[df_info['item'] == field]
                    if not industry_row.empty:
                        industry_name = str(industry_row.iloc[0]['value']).strip()
                        print(f"✅ 找到行业: {field} = {industry_name}")
                        break
                
                if not industry_name:
                    print("⚠️ 未从股票信息中找到行业字段")
            else:
                print("⚠️ 股票基本信息为空")
        except Exception as e:
            print(f"❌ 获取股票基本信息失败: {str(e)}")
            industry_name = None
        
        # 2. 获取行业板块列表
        print("\n[步骤2] 获取行业板块列表...")
        try:
            df_industry_board = ak.stock_board_industry_name_em()
            if df_industry_board is not None and not df_industry_board.empty:
                print(f"✅ 成功获取行业板块列表（共{len(df_industry_board)}个行业）")
                print(f"前5个行业:")
                for idx, row in df_industry_board.head(5).iterrows():
                    print(f"  - {row.get('板块名称', 'N/A')} ({row.get('板块代码', 'N/A')})")
                
                # 如果找到行业名称，尝试匹配
                if industry_name and industry_name != '未知':
                    print(f"\n[步骤3] 匹配行业 '{industry_name}'...")
                    matched = df_industry_board[df_industry_board['板块名称'] == industry_name]
                    if matched.empty:
                        matched = df_industry_board[df_industry_board['板块名称'].str.contains(industry_name, na=False)]
                    
                    if not matched.empty:
                        industry_code = matched.iloc[0].get('板块代码', '')
                        print(f"✅ 找到匹配的行业代码: {industry_code}")
                        
                        # 3. 获取行业成分股
                        print(f"\n[步骤4] 获取行业成分股 (代码: {industry_code})...")
                        try:
                            df_industry_stocks = ak.stock_board_industry_cons_em(symbol=industry_code)
                            if df_industry_stocks is not None and not df_industry_stocks.empty:
                                print(f"✅ 成功获取行业成分股（共{len(df_industry_stocks)}只）")
                                print(f"前5只股票:")
                                for idx, row in df_industry_stocks.head(5).iterrows():
                                    print(f"  - {row.get('名称', 'N/A')} ({row.get('代码', 'N/A')}) "
                                          f"- 价格: {row.get('最新价', 'N/A')}元 "
                                          f"- 涨跌幅: {row.get('涨跌幅', 'N/A')}%")
                                return True
                            else:
                                print("⚠️ 行业成分股为空")
                        except Exception as e:
                            print(f"❌ 获取行业成分股失败: {str(e)}")
                    else:
                        print(f"⚠️ 未找到匹配的行业")
                else:
                    print(f"⚠️ 无法使用行业名称匹配，将尝试反向查找...")
                    print(f"\n[步骤3] 通过成分股反向查找行业...")
                    found = False
                    for idx, row in df_industry_board.head(5).iterrows():  # 只测试前5个
                        test_industry_code = row.get('板块代码', '')
                        test_industry_name = row.get('板块名称', '')
                        if not test_industry_code:
                            continue
                        
                        print(f"  测试行业: {test_industry_name} ({test_industry_code})...")
                        try:
                            df_test_stocks = ak.stock_board_industry_cons_em(symbol=test_industry_code)
                            if df_test_stocks is not None and not df_test_stocks.empty:
                                stock_codes_in_industry = df_test_stocks['代码'].astype(str).str.zfill(6)
                                if clean_code in stock_codes_in_industry.values:
                                    print(f"  ✅ 找到匹配的行业: {test_industry_name} ({test_industry_code})")
                                    found = True
                                    break
                        except Exception as e:
                            print(f"  ⚠️ 测试失败: {str(e)}")
                            continue
                    
                    if not found:
                        print("  ⚠️ 未找到匹配的行业（仅测试了前5个）")
                        return False
                    else:
                        return True
            else:
                print("❌ 行业板块列表为空")
                return False
        except Exception as e:
            print(f"❌ 获取行业板块列表失败: {str(e)}")
            import traceback
            traceback.print_exc()
            return False
            
    except ImportError:
        print("❌ 无法导入akshare库，请先安装: pip install akshare")
        return False
    except Exception as e:
        print(f"❌ 发生错误: {str(e)}")
        import traceback
        traceback.print_exc()
        return False

if __name__ == "__main__":
    print("="*60)
    print("行业数据获取功能测试")
    print("="*60)
    
    # 测试的股票代码
    test_codes = ["300474", "000001", "600000"]
    
    # 首先检查Python服务是否运行
    print("\n[步骤1] 检查Python服务是否运行...")
    python_service_url = os.getenv("PYTHON_DATA_SERVICE_URL", "http://localhost:5001")
    try:
        health_response = requests.get(f"{python_service_url}/health", timeout=5)
        if health_response.status_code == 200:
            print(f"✅ Python服务运行正常 ({python_service_url})")
            test_via_api = True
        else:
            print(f"⚠️ Python服务返回异常状态码: {health_response.status_code}")
            test_via_api = False
    except:
        print(f"⚠️ 无法连接到Python服务 ({python_service_url})")
        print("   将只测试直接调用AKShare的方式")
        test_via_api = False
    
    # 测试每个股票代码
    for stock_code in test_codes:
        print(f"\n\n{'#'*60}")
        print(f"测试股票: {stock_code}")
        print(f"{'#'*60}")
        
        # 如果Python服务可用，测试API接口
        if test_via_api:
            print("\n[测试方式1] 通过Python服务API测试")
            test_industry_api(stock_code)
        
        # 直接测试AKShare
        print("\n[测试方式2] 直接调用AKShare测试")
        test_direct_akshare(stock_code)
        
        print("\n" + "="*60)
        input("按Enter键继续下一个测试...")

