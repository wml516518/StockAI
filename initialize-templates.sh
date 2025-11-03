#!/bin/bash
# 初始化优化选股模板脚本（Linux/Mac 版本）
# 使用方法：chmod +x initialize-templates.sh && ./initialize-templates.sh

API_URL="http://localhost:5000/api/ScreenTemplate/initialize-optimized"

echo "正在初始化优化选股模板..."
echo "API 地址: $API_URL"
echo

# 检查 curl 是否可用
if ! command -v curl &> /dev/null; then
    echo "错误: 未找到 curl 命令，请先安装 curl"
    exit 1
fi

# 执行 POST 请求
response=$(curl -s -w "\n%{http_code}" -X POST "$API_URL" \
    -H "Content-Type: application/json")

# 分离响应体和状态码
http_code=$(echo "$response" | tail -n1)
body=$(echo "$response" | sed '$d')

if [ "$http_code" -eq 200 ]; then
    echo "✅ 模板初始化成功！"
    echo "$body" | grep -o '"updated":[0-9]*' | sed 's/"updated"://' | xargs -I {} echo "   - 已更新: {} 个模板"
    echo "$body" | grep -o '"created":[0-9]*' | sed 's/"created"://' | xargs -I {} echo "   - 已创建: {} 个模板"
    echo
    echo "完成！"
    exit 0
else
    echo "❌ 请求失败！"
    echo "HTTP 状态码: $http_code"
    echo "响应: $body"
    echo
    echo "提示: 请确保 API 服务正在运行 (http://localhost:5000)"
    exit 1
fi

