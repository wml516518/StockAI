#!/bin/bash

# ====================================================
# 股票分析系统部署状态检查脚本
# 检查所有服务和配置是否正常
# ====================================================

echo "=== 股票分析系统部署状态检查 ==="
echo ""

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# 检查函数
check_service() {
    local service_name=$1
    local display_name=$2

    echo -n "检查 $display_name..."
    if sudo systemctl is-active --quiet $service_name; then
        echo -e " ${GREEN}✓ 运行中${NC}"
        return 0
    else
        echo -e " ${RED}✗ 未运行${NC}"
        return 1
    fi
}

check_port() {
    local port=$1
    local service_name=$2

    echo -n "检查端口 $port ($service_name)..."
    if sudo netstat -tlnp 2>/dev/null | grep -q ":$port "; then
        echo -e " ${GREEN}✓ 监听中${NC}"
        return 0
    else
        echo -e " ${RED}✗ 未监听${NC}"
        return 1
    fi
}

check_http() {
    local url=$1
    local service_name=$2

    echo -n "检查 $service_name..."
    if curl -s --max-time 5 $url > /dev/null; then
        echo -e " ${GREEN}✓ 正常${NC}"
        return 0
    else
        echo -e " ${RED}✗ 异常${NC}"
        return 1
    fi
}

check_file() {
    local file_path=$1
    local display_name=$2

    echo -n "检查 $display_name..."
    if [ -f "$file_path" ]; then
        echo -e " ${GREEN}✓ 存在${NC}"
        return 0
    else
        echo -e " ${RED}✗ 不存在${NC}"
        return 1
    fi
}

# 1. 检查系统服务
echo "=== 服务状态检查 ==="
check_service "stock-backend" "后端API服务"
check_service "stock-python" "Python数据服务"
check_service "nginx" "Nginx Web服务器"
check_service "firewalld" "防火墙服务"
echo ""

# 2. 检查端口监听
echo "=== 端口监听检查 ==="
check_port "80" "HTTP"
check_port "5000" "后端API"
check_port "5001" "Python服务"
echo ""

# 3. 检查HTTP访问
echo "=== HTTP访问检查 ==="
SERVER_IP=$(curl -s ifconfig.me 2>/dev/null || echo "localhost")

check_http "http://localhost:5000/health" "后端API健康检查"
check_http "http://localhost:5001/health" "Python服务健康检查"
check_http "http://localhost/" "前端界面"
check_http "http://localhost/swagger" "API文档"
echo ""

# 4. 检查文件存在性
echo "=== 文件存在性检查 ==="
check_file "/opt/stock-analyse/publish/backend/StockAnalyse.Api.dll" "后端程序文件"
check_file "/opt/stock-analyse/frontend/dist/index.html" "前端构建文件"
check_file "/opt/stock-analyse/python-data-service/stock_data_service.py" "Python服务文件"
check_file "/etc/nginx/conf.d/stock-analyse.conf" "Nginx配置文件"
check_file "/opt/stock-analyse/publish/backend/stockanalyse.db" "数据库文件"
echo ""

# 5. 检查系统资源
echo "=== 系统资源检查 ==="
echo "磁盘使用情况:"
df -h /opt/stock-analyse 2>/dev/null || df -h | head -2
echo ""

echo "内存使用情况:"
free -h
echo ""

echo "CPU负载:"
uptime
echo ""

# 6. 检查服务日志（最近的错误）
echo "=== 最近服务日志检查 ==="
echo "后端API服务最近错误:"
sudo journalctl -u stock-backend -n 5 --no-pager -q 2>/dev/null | grep -i error | tail -3 || echo "无错误日志"
echo ""

echo "Python服务最近错误:"
sudo journalctl -u stock-python -n 5 --no-pager -q 2>/dev/null | grep -i error | tail -3 || echo "无错误日志"
echo ""

echo "Nginx最近错误:"
sudo tail -5 /var/log/nginx/error.log 2>/dev/null | grep -v "^\s*$" || echo "无错误日志"
echo ""

# 7. 网络连接测试
echo "=== 网络连接测试 ==="
echo -n "测试外网连接..."
if ping -c 1 -W 2 8.8.8.8 >/dev/null 2>&1; then
    echo -e " ${GREEN}✓ 正常${NC}"
else
    echo -e " ${RED}✗ 异常${NC}"
fi

echo -n "测试DNS解析..."
if nslookup github.com >/dev/null 2>&1; then
    echo -e " ${GREEN}✓ 正常${NC}"
else
    echo -e " ${RED}✗ 异常${NC}"
fi
echo ""

# 8. 总结报告
echo "=== 部署状态总结 ==="
TOTAL_CHECKS=15
PASSED_CHECKS=0

# 计算通过的检查数
check_service "stock-backend" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_service "stock-python" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_service "nginx" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_port "80" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_port "5000" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_port "5001" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_http "http://localhost:5000/health" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_http "http://localhost/" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_file "/opt/stock-analyse/publish/backend/StockAnalyse.Api.dll" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_file "/opt/stock-analyse/frontend/dist/index.html" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_file "/opt/stock-analyse/python-data-service/stock_data_service.py" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_file "/etc/nginx/conf.d/stock-analyse.conf" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
check_file "/opt/stock-analyse/publish/backend/stockanalyse.db" "temp" >/dev/null 2>&1 && ((PASSED_CHECKS++))
ping -c 1 -W 2 8.8.8.8 >/dev/null 2>&1 && ((PASSED_CHECKS++))
nslookup github.com >/dev/null 2>&1 && ((PASSED_CHECKS++))

SUCCESS_RATE=$((PASSED_CHECKS * 100 / TOTAL_CHECKS))

if [ $SUCCESS_RATE -eq 100 ]; then
    echo -e "${GREEN}🎉 所有检查通过！系统运行正常${NC}"
elif [ $SUCCESS_RATE -ge 80 ]; then
    echo -e "${YELLOW}⚠️ 大部分检查通过，系统基本正常${NC}"
else
    echo -e "${RED}❌ 多项检查失败，请检查系统配置${NC}"
fi

echo "通过检查: $PASSED_CHECKS/$TOTAL_CHECKS ($SUCCESS_RATE%)"
echo ""

# 9. 访问信息
echo "=== 访问信息 ==="
if [ "$SERVER_IP" != "localhost" ]; then
    echo "服务器IP: $SERVER_IP"
    echo "前端界面: http://$SERVER_IP"
    echo "API文档: http://$SERVER_IP/swagger"
    echo "健康检查: http://$SERVER_IP/health"
else
    echo "前端界面: http://localhost"
    echo "API文档: http://localhost/swagger"
    echo "健康检查: http://localhost/health"
fi
echo ""

echo "=== 检查完成 ==="
echo "如有问题，请查看上方详细信息或运行以下命令获取更多信息:"
echo "sudo journalctl -u stock-backend -f    # 查看后端日志"
echo "sudo journalctl -u stock-python -f     # 查看Python服务日志"
echo "sudo tail -f /var/log/nginx/error.log  # 查看Nginx错误日志"
