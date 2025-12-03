import requests

# Test Tencent Finance API
stock_code = "002457"
market_prefix = 'sz'
symbol = f"{market_prefix}{stock_code}"
url = f"http://qt.gtimg.cn/q={symbol}"

response = requests.get(url, timeout=10)
content = response.text
print(f"Raw response:\n{content}\n")

# Parse the data
parts = content.split('~')
print(f"Total fields: {len(parts)}")
print(f"\nFirst 60 fields:")
for i, part in enumerate(parts[:60]):
    print(f"Field {i}: {part}")
