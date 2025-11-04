#!/bin/bash

echo "========================================"
echo "启动股票数据服务 (Python + AKShare)"
echo "========================================"
echo ""

# 检查Python是否安装
if ! command -v python3 &> /dev/null; then
    echo "[错误] 未检测到Python，请先安装Python 3.8+"
    exit 1
fi

echo "[1/3] 检查依赖..."
if ! python3 -c "import flask" &> /dev/null; then
    echo "[2/3] 安装依赖包..."
    pip3 install -r requirements.txt
    if [ $? -ne 0 ]; then
        echo "[错误] 依赖安装失败，请检查网络连接"
        exit 1
    fi
else
    echo "[2/3] 依赖已安装"
fi

echo "[3/3] 启动服务..."
echo ""
echo "服务将在 http://localhost:5001 启动"
echo "按 Ctrl+C 停止服务"
echo ""
python3 stock_data_service.py

