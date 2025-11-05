"""
测试AKShare API接口获取股票基本面数据
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

# 测试的股票代码列表
TEST_STOCKS = [
    "000001",  # 平安银行
    "600000",  # 浦发银行
    "000002",  # 万科A
    "600519",  # 贵州茅台
]

def test_health_check():
    """测试健康检查接口"""
    print("=" * 60)
    print("测试1: 健康检查")
    print("=" * 60)
    try:
        response = requests.get("http://localhost:5001/health", timeout=5)
        print(f"状态码: {response.status_code}")
        print(f"响应: {json.dumps(response.json(), indent=2, ensure_ascii=False)}")
        print("✅ 健康检查通过\n")
        return True
    except requests.exceptions.ConnectionError:
        print("❌ 无法连接到服务，请确保Python服务已启动")
        print("   启动命令: python stock_data_service.py")
        return False
    except Exception as e:
        print(f"❌ 健康检查失败: {str(e)}\n")
        return False

def test_single_stock(stock_code):
    """测试获取单个股票基本面数据"""
    print("=" * 60)
    print(f"测试2: 获取股票 {stock_code} 的基本面数据")
    print("=" * 60)
    try:
        url = f"http://localhost:5001/api/stock/fundamental/{stock_code}"
        print(f"请求URL: {url}")
        
        response = requests.get(url, timeout=30)
        print(f"状态码: {response.status_code}")
        
        if response.status_code == 200:
            data = response.json()
            if data.get('success'):
                stock_data = data.get('data', {})
                print("\n✅ 成功获取数据:")
                print(f"  股票代码: {stock_data.get('stockCode')}")
                print(f"  股票名称: {stock_data.get('stockName', '未知')}")
                print(f"  报告期: {stock_data.get('reportDate', '未知')}")
                print(f"\n  盈利能力指标:")
                print(f"    净资产收益率(ROE): {stock_data.get('roe', 'N/A')}%")
                print(f"    销售毛利率: {stock_data.get('grossProfitMargin', 'N/A')}%")
                print(f"    销售净利率: {stock_data.get('netProfitMargin', 'N/A')}%")
                print(f"\n  每股指标:")
                print(f"    基本每股收益(EPS): {stock_data.get('eps', 'N/A')} 元")
                print(f"    每股净资产(BPS): {stock_data.get('bps', 'N/A')} 元")
                print(f"\n  财务数据:")
                print(f"    营业总收入: {stock_data.get('totalRevenue', 'N/A')} 万元")
                print(f"    净利润: {stock_data.get('netProfit', 'N/A')} 万元")
                print(f"\n  成长性指标:")
                print(f"    营业收入同比增长率: {stock_data.get('revenueGrowthRate', 'N/A')}%")
                print(f"    净利润同比增长率: {stock_data.get('profitGrowthRate', 'N/A')}%")
                print(f"\n  偿债能力指标:")
                print(f"    资产负债率: {stock_data.get('assetLiabilityRatio', 'N/A')}%")
                print(f"    流动比率: {stock_data.get('currentRatio', 'N/A')}")
                print(f"    速动比率: {stock_data.get('quickRatio', 'N/A')}")
                print(f"\n  运营能力指标:")
                print(f"    存货周转率: {stock_data.get('inventoryTurnover', 'N/A')}")
                print(f"    应收账款周转率: {stock_data.get('accountsReceivableTurnover', 'N/A')}")
                print(f"\n  数据来源: {stock_data.get('source', '未知')}")
                print(f"  更新时间: {stock_data.get('lastUpdate', '未知')}")
                
                print("\n完整JSON数据:")
                print(json.dumps(data, indent=2, ensure_ascii=False))
                return True
            else:
                print(f"❌ 请求失败: {data.get('error', '未知错误')}")
                if 'suggestions' in data:
                    print("建议:")
                    for suggestion in data['suggestions']:
                        print(f"  - {suggestion}")
                return False
        else:
            print(f"❌ HTTP错误: {response.status_code}")
            print(f"响应内容: {response.text}")
            return False
    except requests.exceptions.Timeout:
        print("❌ 请求超时（超过30秒）")
        return False
    except Exception as e:
        print(f"❌ 测试失败: {str(e)}")
        import traceback
        traceback.print_exc()
        return False
    finally:
        print()

def test_batch_stocks(stock_codes):
    """测试批量获取股票基本面数据"""
    print("=" * 60)
    print(f"测试3: 批量获取股票基本面数据")
    print(f"股票代码: {', '.join(stock_codes)}")
    print("=" * 60)
    try:
        url = "http://localhost:5001/api/stock/batch"
        payload = {"stockCodes": stock_codes}
        
        print(f"请求URL: {url}")
        print(f"请求数据: {json.dumps(payload, indent=2, ensure_ascii=False)}")
        
        response = requests.post(url, json=payload, timeout=60)
        print(f"状态码: {response.status_code}")
        
        if response.status_code == 200:
            data = response.json()
            if data.get('success'):
                results = data.get('data', [])
                print(f"\n✅ 成功获取 {len(results)} 只股票的数据:")
                for stock in results:
                    print(f"\n  [{stock.get('stockCode')}] {stock.get('stockName', '未知')}")
                    print(f"    ROE: {stock.get('roe', 'N/A')}%")
                    print(f"    净利润: {stock.get('netProfit', 'N/A')} 万元")
                    print(f"    营业收入增长率: {stock.get('revenueGrowthRate', 'N/A')}%")
                return True
            else:
                print(f"❌ 批量请求失败: {data.get('error', '未知错误')}")
                return False
        else:
            print(f"❌ HTTP错误: {response.status_code}")
            print(f"响应内容: {response.text}")
            return False
    except Exception as e:
        print(f"❌ 批量测试失败: {str(e)}")
        import traceback
        traceback.print_exc()
        return False
    finally:
        print()

def main():
    """主测试函数"""
    print("\n" + "=" * 60)
    print("AKShare API 测试脚本")
    print(f"测试时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 60 + "\n")
    
    # 测试1: 健康检查
    if not test_health_check():
        print("\n⚠️  服务未启动，请先运行: python stock_data_service.py")
        return
    
    # 测试2: 逐个测试股票
    success_count = 0
    for stock_code in TEST_STOCKS:
        if test_single_stock(stock_code):
            success_count += 1
    
    print("=" * 60)
    print(f"单个股票测试结果: {success_count}/{len(TEST_STOCKS)} 成功")
    print("=" * 60 + "\n")
    
    # 测试3: 批量测试
    test_batch_stocks(TEST_STOCKS[:2])  # 只测试前2个，避免超时
    
    print("=" * 60)
    print("测试完成！")
    print("=" * 60)

if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n测试被用户中断")
    except Exception as e:
        print(f"\n\n❌ 测试过程中发生错误: {str(e)}")
        import traceback
        traceback.print_exc()

