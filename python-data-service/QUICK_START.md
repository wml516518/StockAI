# 快速启动指南

## ✅ 依赖已安装

所有Python依赖已成功安装！

## 🚀 启动服务

### 方法1：使用PowerShell脚本（推荐）

```powershell
cd D:\Demo\StockAnalyse\python-data-service
.\start-service.ps1
```

### 方法2：直接运行Python

```powershell
cd D:\Demo\StockAnalyse\python-data-service
python stock_data_service.py
```

### 方法3：使用批处理文件

```powershell
cd D:\Demo\StockAnalyse\python-data-service
cmd /c start-service.bat
```

## ✅ 验证服务

服务启动后，打开浏览器访问：

**健康检查**: http://localhost:5001/health

应该看到：
```json
{"status": "ok", "service": "stock-data-service"}
```

**测试API**: http://localhost:5001/api/stock/fundamental/000001

## 📝 注意事项

1. 服务默认运行在 `http://localhost:5001`
2. 首次运行可能需要下载数据，请耐心等待
3. 按 `Ctrl+C` 停止服务
4. 确保5001端口未被占用

## 🔧 故障排除

### 端口被占用

如果5001端口被占用，修改 `stock_data_service.py` 最后一行：

```python
app.run(host='0.0.0.0', port=5002, debug=True)  # 改为5002或其他端口
```

然后更新C#代码中的服务地址。

### Python命令找不到

确保Python已添加到PATH，或使用完整路径：

```powershell
C:\Users\YourName\AppData\Local\Programs\Python\Python314\python.exe stock_data_service.py
```

## 🎯 下一步

服务启动后，启动C#后端服务，系统会自动使用Python服务获取财务数据！

