import requests
import json

def test_endpoint(url):
    print(f"Testing POST {url}...")
    try:
        response = requests.post(url, json={}, timeout=10)
        print(f"Status Code: {response.status_code}")
        print(f"Response: {response.text[:200]}")
        return response.status_code
    except Exception as e:
        print(f"Error: {e}")
        return None

base_url = "http://localhost:5000/api"

# Test 1: Exact case as in controller (usually PascalCase for controller, but route might be lowercase)
# Controller: [Route("api/[controller]")] -> api/Screen
# Action: [HttpPost("auto-selection/execute")]
url1 = f"{base_url}/Screen/auto-selection/execute"
test_endpoint(url1)

# Test 2: Lowercase (what frontend sends)
url2 = f"{base_url}/screen/auto-selection/execute"
test_endpoint(url2)

# Test 3: Try GET just to see if it returns 405 (Method Not Allowed) which confirms route exists
print(f"Testing GET {url2}...")
try:
    response = requests.get(url2, timeout=10)
    print(f"Status Code: {response.status_code}") # Should be 405 if route exists but POST only
except Exception as e:
    print(f"Error: {e}")
