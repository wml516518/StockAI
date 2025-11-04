# 第三方数据源集成指南

## 🚀 快速开始

### 方案1：使用Python服务（AKShare）- 推荐 ⭐⭐⭐⭐⭐

**优点**：
- ✅ 完全免费，无需注册
- ✅ 数据源丰富（聚合多个数据源）
- ✅ 数据质量高

**步骤**：

#### 1. 安装Python和依赖

```bash
# 进入Python服务目录
cd python-data-service

# Windows
start-service.bat

# Linux/Mac
chmod +x start-service.sh
./start-service.sh
```

或手动安装：

```bash
# 安装Python依赖
pip install -r requirements.txt

# 启动服务
python stock_data_service.py
```

#### 2. 验证服务运行

打开浏览器访问：`http://localhost:5001/health`

应该返回：
```json
{"status": "ok", "service": "stock-data-service"}
```

#### 3. 测试API

```bash
# 测试获取股票基本面数据
curl http://localhost:5001/api/stock/fundamental/000001
```

#### 4. 配置C#项目

服务默认运行在 `http://localhost:5001`，如果需要修改，可以设置环境变量：

```bash
# Windows PowerShell
$env:PYTHON_DATA_SERVICE_URL="http://localhost:5001"

# Linux/Mac
export PYTHON_DATA_SERVICE_URL="http://localhost:5001"
```

或者直接在 `appsettings.json` 中添加配置（需要修改代码读取配置）。

#### 5. 启动后端服务

Python服务运行后，启动你的C#后端服务，系统会自动尝试从Python服务获取数据。

---

## 📊 数据流程

```
C# API请求
    ↓
GetFundamentalInfoAsync()
    ↓
TryGetFundamentalInfoFromPythonServiceAsync()  ← 优先尝试
    ↓
HTTP请求 → http://localhost:5001/api/stock/fundamental/{code}
    ↓
Python Flask服务
    ↓
AKShare库获取数据
    ↓
返回JSON → C#解析 → StockFundamentalInfo
```

---

## 🔧 故障排除

### Python服务无法启动

1. **检查Python版本**：
   ```bash
   python --version  # 需要3.8+
   ```

2. **检查依赖安装**：
   ```bash
   pip list | grep -E "flask|akshare|pandas"
   ```

3. **端口被占用**：
   - 修改 `stock_data_service.py` 中的端口号
   - 或关闭占用5001端口的程序

### C#无法连接Python服务

1. **检查服务是否运行**：
   ```bash
   curl http://localhost:5001/health
   ```

2. **检查防火墙**：
   - 确保5001端口未被防火墙阻止

3. **检查环境变量**：
   - 确认 `PYTHON_DATA_SERVICE_URL` 设置正确

### 数据获取失败

1. **检查股票代码格式**：
   - 确保是6位数字代码，如 `000001`、`600000`

2. **查看Python服务日志**：
   - 服务会输出详细的错误信息

3. **网络问题**：
   - AKShare需要网络连接下载数据
   - 首次运行可能需要较长时间

---

## 📝 其他数据源集成

### Tushare Pro

如果需要使用Tushare，可以修改Python服务添加Tushare接口：

```python
import tushare as ts

# 在stock_data_service.py中添加
@app.route('/api/stock/fundamental/tushare/<stock_code>')
def get_fundamental_tushare(stock_code):
    ts.set_token('your_token')
    pro = ts.pro_api()
    
    # 获取财务指标
    df = pro.fina_indicator(ts_code=f"{stock_code}.{market_suffix}")
    # ... 处理数据
```

---

## 🎯 最佳实践

1. **启动顺序**：
   - 先启动Python服务
   - 再启动C#后端服务

2. **监控服务**：
   - 定期检查Python服务健康状态
   - 使用 `/health` 接口监控

3. **错误处理**：
   - C#代码已实现自动回退到其他数据源
   - 如果Python服务不可用，会自动使用备用方案

4. **性能优化**：
   - Python服务可以缓存常用股票数据
   - 可以添加Redis缓存层

---

## 📚 相关文档

- [AKShare文档](https://akshare.akfamily.xyz/)
- [Flask文档](https://flask.palletsprojects.com/)
- [Python服务README](./python-data-service/README.md)

---

## 💡 提示

- Python服务可以和C#服务运行在同一台机器上
- 也可以部署到独立的服务器
- 建议使用进程管理器（如PM2、Supervisor）管理Python服务

