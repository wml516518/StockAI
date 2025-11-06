"""
测试AKShare的stock_board_industry_name_em方法
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

import pandas as pd
from datetime import datetime
import time

def test_stock_board_industry_name_em():
    """
    测试stock_board_industry_name_em方法
    """
    print("="*60)
    print("测试 stock_board_industry_name_em 方法")
    print("="*60)
    print(f"开始时间: {datetime.now()}\n")
    
    # 禁用代理（如果存在）
    original_proxies = {}
    for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
        original_proxies[proxy_var] = os.environ.get(proxy_var)
        os.environ.pop(proxy_var, None)
    
    # 设置NO_PROXY以禁止所有代理
    os.environ['NO_PROXY'] = '*'
    os.environ['no_proxy'] = '*'
    
    print("✅ 已临时禁用代理环境变量")
    print("✅ 已设置 NO_PROXY=*\n")
    
    # 使用monkey patch彻底禁用代理（与Python服务保持一致）
    try:
        import requests
        import urllib3
        urllib3.disable_warnings()
        
        # 保存原始的requests方法
        _original_get = requests.get
        _original_session_init = requests.Session.__init__
        
        # Monkey patch: 强制禁用代理并添加请求头
        def patched_get(*args, **kwargs):
            kwargs['proxies'] = {'http': None, 'https': None}
            kwargs.setdefault('timeout', 60)  # 增加超时时间
            
            # 添加请求头，模拟浏览器
            headers = kwargs.get('headers', {})
            if not headers:
                headers = {}
            headers.setdefault('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36')
            headers.setdefault('Accept', 'application/json, text/plain, */*')
            headers.setdefault('Accept-Language', 'zh-CN,zh;q=0.9,en;q=0.8')
            headers.setdefault('Referer', 'http://quote.eastmoney.com/')
            headers.setdefault('Origin', 'http://quote.eastmoney.com')
            kwargs['headers'] = headers
            
            return _original_get(*args, **kwargs)
        
        def patched_session_init(self, *args, **kwargs):
            _original_session_init(self, *args, **kwargs)
            self.trust_env = False
            self.proxies = {'http': None, 'https': None}
        
        # 应用monkey patch
        requests.get = patched_get
        requests.Session.__init__ = patched_session_init
        
        print("✅ 已通过monkey patch禁用requests代理（包括系统代理）\n")
    except Exception as e:
        print(f"⚠️ 配置requests代理设置时出错: {str(e)}\n")
    
    try:
        import akshare as ak
        
        print("[步骤1] 调用 stock_board_industry_name_em()...")
        print("正在获取行业板块列表，请稍候...\n")
        
        start_time = time.time()
        
        # 带重试机制的调用
        df_result = None
        max_retries = 3
        last_error = None
        
        for attempt in range(max_retries):
            try:
                # 每次重试前增加延迟，避免请求过快
                if attempt > 0:
                    delay = 2.0 * attempt  # 第2次重试延迟2秒，第3次延迟4秒
                    print(f"⏳ 等待{delay:.1f}秒后重试...")
                    time.sleep(delay)
                
                print(f"尝试 {attempt + 1}/{max_retries}...")
                
                # 在调用前再次确保禁用代理
                import requests
                import urllib3
                urllib3.disable_warnings()
                
                df_result = ak.stock_board_industry_name_em()
                
                if df_result is not None and not df_result.empty:
                    elapsed_time = time.time() - start_time
                    print(f"✅ 成功获取数据！耗时: {elapsed_time:.2f}秒\n")
                    break
                else:
                    print("⚠️ 返回数据为空\n")
                    if attempt < max_retries - 1:
                        time.sleep(1)
                    else:
                        print("❌ 所有重试均返回空数据")
                        return False
            except Exception as e:
                last_error = e
                elapsed_time = time.time() - start_time
                print(f"❌ 尝试 {attempt + 1} 失败: {str(e)[:100]}")
                
                if attempt < max_retries - 1:
                    print(f"等待1秒后重试...\n")
                    time.sleep(1)
                else:
                    print(f"\n❌ 所有重试均失败")
                    print(f"最终错误: {str(e)}")
                    import traceback
                    traceback.print_exc()
                    return False
        
        if df_result is None or df_result.empty:
            print("❌ 未能获取到数据")
            return False
        
        # 分析返回的数据
        print("="*60)
        print("数据统计")
        print("="*60)
        print(f"总行数: {len(df_result)}")
        print(f"总列数: {len(df_result.columns)}")
        print(f"列名: {list(df_result.columns)}")
        
        # 检查关键列是否存在
        required_columns = ['板块名称', '板块代码']
        missing_columns = [col for col in required_columns if col not in df_result.columns]
        if missing_columns:
            print(f"\n⚠️ 缺少关键列: {missing_columns}")
            print(f"实际列名: {list(df_result.columns)}")
        else:
            print("\n✅ 所有关键列都存在")
        
        # 显示前10行数据
        print("\n" + "="*60)
        print("前10个行业板块")
        print("="*60)
        for idx, row in df_result.head(10).iterrows():
            industry_name = row.get('板块名称', 'N/A')
            industry_code = row.get('板块代码', 'N/A')
            print(f"{idx + 1:3d}. {industry_name:30s} (代码: {industry_code})")
        
        # 显示数据示例（完整的一行）
        print("\n" + "="*60)
        print("第一行完整数据示例")
        print("="*60)
        if len(df_result) > 0:
            first_row = df_result.iloc[0]
            for col in df_result.columns:
                value = first_row.get(col, 'N/A')
                print(f"{col:20s}: {value}")
        
        # 统计信息
        print("\n" + "="*60)
        print("数据质量检查")
        print("="*60)
        
        if '板块名称' in df_result.columns:
            non_null_names = df_result['板块名称'].notna().sum()
            print(f"板块名称非空: {non_null_names}/{len(df_result)} ({non_null_names/len(df_result)*100:.1f}%)")
        else:
            print("⚠️ 未找到'板块名称'列")
        
        if '板块代码' in df_result.columns:
            non_null_codes = df_result['板块代码'].notna().sum()
            print(f"板块代码非空: {non_null_codes}/{len(df_result)} ({non_null_codes/len(df_result)*100:.1f}%)")
        else:
            print("⚠️ 未找到'板块代码'列")
        
        # 搜索特定行业（如果存在）
        if '板块名称' in df_result.columns:
            print("\n" + "="*60)
            print("行业搜索测试")
            print("="*60)
            
            test_industries = ['电子元件', '银行', '房地产', '软件', '医药']
            for test_industry in test_industries:
                # 精确匹配
                exact_match = df_result[df_result['板块名称'] == test_industry]
                # 包含匹配
                contains_match = df_result[df_result['板块名称'].str.contains(test_industry, na=False)]
                
                if not exact_match.empty:
                    industry_code = exact_match.iloc[0].get('板块代码', 'N/A')
                    print(f"✅ '{test_industry}': 精确匹配 (代码: {industry_code})")
                elif not contains_match.empty:
                    matched_names = contains_match['板块名称'].tolist()
                    print(f"✅ '{test_industry}': 找到 {len(contains_match)} 个包含该名称的行业")
                    for name in matched_names[:3]:  # 只显示前3个
                        code = contains_match[contains_match['板块名称'] == name].iloc[0].get('板块代码', 'N/A')
                        print(f"   - {name} (代码: {code})")
                else:
                    print(f"❌ '{test_industry}': 未找到")
        
        # 保存数据到CSV（可选）
        save_csv = input("\n是否保存数据到CSV文件? (y/n): ").strip().lower()
        if save_csv == 'y':
            filename = f"industry_board_list_{datetime.now().strftime('%Y%m%d_%H%M%S')}.csv"
            df_result.to_csv(filename, index=False, encoding='utf-8-sig')
            print(f"✅ 数据已保存到: {filename}")
        
        print("\n" + "="*60)
        print("测试完成!")
        print("="*60)
        return True
        
    except ImportError:
        print("❌ 无法导入akshare库")
        print("请先安装: pip install akshare")
        return False
    except Exception as e:
        print(f"\n❌ 发生未预期的错误: {str(e)}")
        import traceback
        traceback.print_exc()
        return False
    finally:
        # 恢复代理设置
        for proxy_var, value in original_proxies.items():
            if value:
                os.environ[proxy_var] = value
        print("\n✅ 已恢复代理设置")

def test_industry_code_lookup(industry_name):
    """
    测试根据行业名称查找行业代码
    """
    print("\n" + "="*60)
    print(f"测试行业名称查找: '{industry_name}'")
    print("="*60)
    
    # 禁用代理
    original_proxies = {}
    for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
        original_proxies[proxy_var] = os.environ.get(proxy_var)
        os.environ.pop(proxy_var, None)
    
    try:
        import akshare as ak
        
        # 获取行业板块列表
        print("正在获取行业板块列表...")
        df_industry_board = ak.stock_board_industry_name_em()
        
        if df_industry_board is None or df_industry_board.empty:
            print("❌ 无法获取行业板块列表")
            return False
        
        print(f"✅ 获取到 {len(df_industry_board)} 个行业板块\n")
        
        # 精确匹配
        exact_match = df_industry_board[df_industry_board['板块名称'] == industry_name]
        if not exact_match.empty:
            industry_code = exact_match.iloc[0].get('板块代码', '')
            print(f"✅ 精确匹配成功!")
            print(f"   行业名称: {industry_name}")
            print(f"   行业代码: {industry_code}")
            return industry_code
        
        # 包含匹配
        contains_match = df_industry_board[df_industry_board['板块名称'].str.contains(industry_name, na=False)]
        if not contains_match.empty:
            print(f"⚠️ 未找到精确匹配，但找到 {len(contains_match)} 个包含 '{industry_name}' 的行业:")
            for idx, row in contains_match.iterrows():
                print(f"   - {row.get('板块名称')} (代码: {row.get('板块代码')})")
            return contains_match.iloc[0].get('板块代码', '')
        
        print(f"❌ 未找到包含 '{industry_name}' 的行业")
        return False
        
    except Exception as e:
        print(f"❌ 发生错误: {str(e)}")
        import traceback
        traceback.print_exc()
        return False
    finally:
        # 恢复代理设置
        for proxy_var, value in original_proxies.items():
            if value:
                os.environ[proxy_var] = value

if __name__ == "__main__":
    print("\n选择测试模式:")
    print("1. 测试 stock_board_industry_name_em() 方法")
    print("2. 测试根据行业名称查找行业代码")
    print("3. 执行所有测试")
    
    choice = input("\n请选择 (1/2/3): ").strip()
    
    if choice == "1":
        test_stock_board_industry_name_em()
    elif choice == "2":
        industry_name = input("请输入要查找的行业名称: ").strip()
        if industry_name:
            test_industry_code_lookup(industry_name)
        else:
            print("未输入行业名称")
    elif choice == "3":
        test_stock_board_industry_name_em()
        print("\n" + "="*60)
        industry_name = input("请输入要查找的行业名称（按Enter跳过）: ").strip()
        if industry_name:
            test_industry_code_lookup(industry_name)
    else:
        print("无效选择，将执行默认测试...")
        test_stock_board_industry_name_em()

