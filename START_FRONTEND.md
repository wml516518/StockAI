# 启动前端服务指南

## 问题：无法访问 http://localhost:5173

### 解决方案

#### 方法1：使用批处理文件（推荐）

```cmd
cd frontend
start-dev.bat
```

或从项目根目录：

```cmd
.\start-frontend-only.bat
```

#### 方法2：手动启动

```cmd
cd frontend
npm run dev
```

#### 方法3：使用PowerShell

```powershell
cd D:\Demo\StockAnalyse\frontend
npm run dev
```

## 检查服务状态

### 检查端口是否被占用

```cmd
netstat -ano | findstr :5173
```

如果看到输出，说明端口已被占用。

### 检查Node进程

```powershell
Get-Process node -ErrorAction SilentlyContinue
```

### 检查服务是否运行

访问：http://localhost:5173

或在PowerShell中：

```powershell
Invoke-WebRequest -Uri "http://localhost:5173" -UseBasicParsing
```

## 常见问题

### 1. 端口被占用

如果5173端口被占用，可以：

**方法A：停止占用端口的进程**
```cmd
# 查找占用端口的进程
netstat -ano | findstr :5173

# 停止进程（替换PID）
taskkill /PID <进程ID> /F
```

**方法B：修改端口**

编辑 `frontend/vite.config.js`：

```javascript
export default {
  server: {
    port: 5174  // 改为其他端口
  }
}
```

### 2. 依赖未安装

如果 `node_modules` 不存在：

```cmd
cd frontend
npm install
```

### 3. Node.js环境变量问题

如果提示找不到 `node` 或 `npm`：

```cmd
# 刷新环境变量
set PATH=%PATH%;%ProgramFiles%\nodejs

# 或重新打开命令行窗口
```

### 4. 防火墙阻止

检查Windows防火墙是否阻止了5173端口。

## 验证安装

确认以下内容：

- ✅ Node.js已安装：`node --version`
- ✅ npm已安装：`npm --version`
- ✅ 依赖已安装：`frontend/node_modules` 目录存在
- ✅ vite.config.js 存在

## 启动成功标志

启动成功后，你应该看到：

```
  VITE v5.0.8  ready in xxx ms

  ➜  Local:   http://localhost:5173/
  ➜  Network: use --host to expose
```

然后可以在浏览器中访问 http://localhost:5173

