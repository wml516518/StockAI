# 故障排除指南

## 前端服务无法启动

### 问题1: 批处理文件执行后前端未启动

**症状**: 运行 `start-all-services.bat` 后，前端服务窗口没有出现或立即关闭

**解决方案**:

1. **检查Node.js是否安装**:
   ```cmd
   node --version
   npm --version
   ```
   如果提示找不到命令，需要安装Node.js或刷新环境变量

2. **手动刷新环境变量**:
   ```cmd
   set PATH=%PATH%;%ProgramFiles%\nodejs
   ```

3. **单独启动前端服务**:
   ```cmd
   start-frontend-only.bat
   ```
   或手动启动:
   ```cmd
   cd frontend
   npm run dev
   ```

4. **检查前端依赖**:
   ```cmd
   cd frontend
   npm install
   ```

### 问题2: 前端服务窗口出现但立即关闭

**可能原因**:
- npm命令找不到
- 依赖未安装
- 端口被占用

**解决方案**:

1. **检查npm是否可用**:
   ```cmd
   where npm
   ```

2. **手动安装依赖**:
   ```cmd
   cd frontend
   npm install
   ```

3. **检查端口占用**:
   ```cmd
   netstat -ano | findstr :5173
   ```

### 问题3: 前端服务启动但无法访问

**检查步骤**:

1. 查看前端服务窗口，确认是否有错误信息
2. 检查防火墙是否阻止了5173端口
3. 尝试访问: http://localhost:5173
4. 查看浏览器控制台是否有错误

## Python服务无法启动

### 问题1: Python命令找不到

**解决方案**:
```cmd
# 检查Python是否安装
python --version

# 如果找不到，检查安装路径
dir "%ProgramFiles%\Python*"
```

### 问题2: 依赖安装失败

**解决方案**:
```cmd
cd python-data-service
pip install -r requirements.txt
```

### 问题3: 端口被占用

**解决方案**:
```cmd
# 检查端口占用
netstat -ano | findstr :5001

# 停止占用进程
taskkill /PID <进程ID> /F
```

## 后端API无法启动

### 问题1: .NET SDK未安装

**解决方案**:
- 下载安装: https://dotnet.microsoft.com/download
- 验证: `dotnet --version`

### 问题2: 数据库连接失败

**解决方案**:
- 检查数据库文件是否存在
- 检查连接字符串配置

## 环境变量问题

### 刷新环境变量（Windows）

**方法1: 重新打开命令行窗口**
- 关闭当前窗口
- 重新打开PowerShell或CMD

**方法2: 在PowerShell中刷新**
```powershell
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
```

**方法3: 在CMD中刷新**
```cmd
set PATH=%PATH%;%ProgramFiles%\nodejs
```

## 快速诊断命令

### 检查所有服务状态

```powershell
# 检查Python服务
Invoke-WebRequest -Uri "http://localhost:5001/health" -UseBasicParsing

# 检查后端API
Invoke-WebRequest -Uri "http://localhost:5000/swagger" -UseBasicParsing

# 检查前端服务
Invoke-WebRequest -Uri "http://localhost:5173" -UseBasicParsing
```

### 检查进程

```powershell
# 检查Python进程
Get-Process python -ErrorAction SilentlyContinue

# 检查Node进程
Get-Process node -ErrorAction SilentlyContinue

# 检查dotnet进程
Get-Process dotnet -ErrorAction SilentlyContinue
```

## 手动启动服务（如果自动启动失败）

### 1. Python服务
```cmd
cd python-data-service
python stock_data_service.py
```

### 2. 后端API
```cmd
cd src\StockAnalyse.Api
dotnet run
```

### 3. 前端服务
```cmd
cd frontend
npm run dev
```

## 联系支持

如果以上方法都无法解决问题，请：
1. 查看服务窗口的错误日志
2. 检查系统环境变量配置
3. 确认所有依赖都已正确安装

