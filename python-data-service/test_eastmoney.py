import requests
import json

# Test East Money API
stock_code = "002457"
market_code = '0'  # 深圳
secid = f"{market_code}.{stock_code}"

url = "http://push2.eastmoney.com/api/qt/stock/get"
params = {
    'secid': secid,
    'fields': 'f57,f58,f84,f85,f86,f127,f116,f60,f45,f46,f47,f48,f50,f107,f137,f138,f139,f140,f141,f142,f162'
}

try:
    response = requests.get(url, params=params, timeout=10)
    response.raise_for_status()
    
    data = response.json()
    print("East Money Response:")
    print(json.dumps(data, indent=2, ensure_ascii=False))
    
    if data and 'data' in data:
        stock_data = data['data']
        print(f"\nf127 (行业): {stock_data.get('f127')}")
        print(f"\nAll fields:")
        for key, value in stock_data.items():
            print(f"  {key}: {value}")
except Exception as e:
    print(f"Error: {e}")
