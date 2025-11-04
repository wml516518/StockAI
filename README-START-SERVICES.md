# 一键启动服务指南

## 🚀 快速开始

### Windows 批处理文件（推荐）

```cmd
# 启动所有服务
start-all-services.bat

# 停止所有服务
stop-all-services.bat
```

### PowerShell 脚本

```powershell
# 启动所有服务
.\start-all-services.ps1

# 停止所有服务
.\stop-all-services.ps1
```

## 📋 启动的服务

脚本会自动启动以下服务：

1. **Python数据服务** (端口 5001)
   - 提供股票财务数据API
   - 使用AKShare数据源
   - 地址: http://localhost:5001

2. **后端API服务** (端口 5000)
   - .NET Core API服务
   - 提供RESTful API接口
   - 地址: http://localhost:5000
   - API文档: http://localhost:5000/swagger

3. **前端开发服务器** (端口 5173)
   - Vue 3 + Vite 开发服务器
   - 热更新支持
   - 地址: http://localhost:5173

## ⚙️ 前置要求

### 必需环境

- **.NET 8.0 SDK** - 后端API需要
  - 下载: https://dotnet.microsoft.com/download
  - 验证: `dotnet --version`

- **Python 3.8+** - Python数据服务需要
  - 下载: https://www.python.org/downloads/
  - 验证: `python --version`
  - 依赖: `pip install -r python-data-service/requirements.txt`

- **Node.js 16+** - 前端开发服务器需要
  - 下载: https://nodejs.org/
  - 验证: `node --version`
  - 依赖: `cd frontend && npm install`

## 🔍 服务状态检查

### 检查Python服务
```powershell
Invoke-WebRequest -Uri "http://localhost:5001/health"
```

### 检查后端API
```powershell
Invoke-WebRequest -Uri "http://localhost:5000/swagger"
```

### 检查前端服务
```powershell
Invoke-WebRequest -Uri "http://localhost:5173"
```

## 🛠️ 手动启动（如果自动启动失败）

### 1. 启动Python服务
```powershell
cd python-data-service
python stock_data_service.py
```

### 2. 启动后端API（新窗口）
```powershell
cd src\StockAnalyse.Api
dotnet run
```

### 3. 启动前端服务（新窗口）
```powershell
cd frontend
npm run dev
```

## ❌ 故障排除

### Python服务无法启动

1. 检查Python是否安装: `python --version`
2. 检查依赖是否安装: `pip list | findstr flask`
3. 检查端口是否被占用: `netstat -ano | findstr :5001`

### 后端API无法启动

1. 检查.NET SDK: `dotnet --version`
2. 检查数据库连接
3. 检查端口是否被占用: `netstat -ano | findstr :5000`

### 前端服务无法启动

1. 检查Node.js: `node --version`
2. 检查依赖: `cd frontend && npm install`
3. 检查端口是否被占用: `netstat -ano | findstr :5173`

### 端口被占用

如果端口被占用，可以：

1. **停止占用端口的进程**:
   ```powershell
   # 查找占用端口的进程
   netstat -ano | findstr :5001
   
   # 停止进程（替换PID）
   taskkill /PID <进程ID> /F
   ```

2. **修改服务端口**:
   - Python服务: 修改 `python-data-service/stock_data_service.py` 中的端口
   - 后端API: 修改 `src/StockAnalyse.Api/Properties/launchSettings.json`
   - 前端服务: 修改 `frontend/vite.config.js`

## 📝 注意事项

1. **服务启动顺序**: 
   - Python服务 → 后端API → 前端服务
   - 脚本会自动按顺序启动并等待

2. **服务窗口**: 
   - 每个服务在独立窗口中运行
   - 关闭窗口会停止对应服务
   - 最小化窗口不会影响服务运行

3. **首次运行**: 
   - Python服务首次运行可能需要下载数据
   - 前端服务首次运行需要安装依赖

4. **开发环境**: 
   - 前端服务支持热更新（HMR）
   - 修改代码后自动刷新
   - 后端API需要重启才能看到更改

## 🎯 使用流程

1. **启动所有服务**:
   ```powershell
   .\start-all-services.ps1
   ```

2. **访问应用**:
   - 打开浏览器: http://localhost:5173
   - 查看API文档: http://localhost:5000/swagger

3. **停止所有服务**:
   ```powershell
   .\stop-all-services.ps1
   ```

## 🔗 相关文档

- [集成指南](./INTEGRATION_GUIDE.md)
- [第三方数据源](./THIRD_PARTY_DATA_SOURCES.md)
- [前端快速开始](./frontend/START_HERE.md)

