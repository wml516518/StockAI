# BrowserTools MCP 配置指南

本指南将帮助您将 BrowserTools MCP 集成到 Cursor 中，使用端口 3025。

## 前提条件

1. ✅ 已安装 Chrome 浏览器
2. ✅ 已安装 BrowserTools MCP Chrome 扩展
3. ✅ BrowserTools MCP 服务器正在运行（端口 3025）

## 配置步骤

### 方法一：通过 Cursor 设置界面配置（推荐）

1. **打开 Cursor 设置**
   - 点击左下角的齿轮图标 ⚙️
   - 或使用快捷键：`Ctrl + ,` (Windows) / `Cmd + ,` (Mac)

2. **导航到 MCP 配置**
   - 在设置菜单中，选择 **"Features"** 选项卡
   - 在左侧列表中，点击 **"MCP"** 进入 MCP 配置页面

3. **添加新的 MCP 服务器**
   - 点击 **"Add New MCP Server"** 按钮
   - 填写以下配置：
     - **Name（名称）：** `browser-tools`
     - **Type（类型）：** 选择 `sse`（Server-Sent Events）
     - **Server URL（服务器 URL）：** `http://localhost:3025/sse`

4. **保存配置**
   - 点击 **"Save"** 按钮保存配置

5. **验证连接**
   - 返回 MCP 配置页面
   - 确保 "browser-tools" 服务器显示为 **已连接** 状态（绿色指示）

### 方法二：通过配置文件配置

如果 Cursor 支持通过配置文件配置 MCP，配置文件通常位于：

**Windows:**
```
%APPDATA%\Cursor\User\globalStorage\mcp.json
或
%USERPROFILE%\.cursor\mcp.json
```

**配置文件示例：**

```json
{
  "mcpServers": {
    "browser-tools": {
      "command": "npx",
      "args": ["@agentdeskai/browser-tools-mcp@latest"],
      "env": {
        "BROWSER_TOOLS_HOST": "127.0.0.1",
        "BROWSER_TOOLS_PORT": "3025"
      }
    }
  }
}
```

或者使用 SSE 方式：

```json
{
  "mcpServers": {
    "browser-tools": {
      "type": "sse",
      "url": "http://localhost:3025/sse"
    }
  }
}
```

## 启动 BrowserTools MCP 服务器

在配置 Cursor 之前，确保 BrowserTools MCP 服务器正在运行：

```bash
# 方法1: 使用 npx（推荐）
npx @agentdeskai/browser-tools-mcp@latest

# 方法2: 如果已全局安装
browser-tools-mcp
```

服务器启动后，您应该看到类似输出：
```
=== Browser Tools Server Started ===
Aggregator listening on http://0.0.0.0:3025
Available on the following network addresses:
For local access use: http://localhost:3025
```

## 验证集成

### 1. 在 Chrome 中验证

1. 打开 Chrome 浏览器
2. 按 `F12` 或 `Ctrl+Shift+I` 打开开发者工具
3. 导航到 **"BrowserToolsMCP"** 面板
4. 点击 **"Test connection"** 按钮
5. 确保连接状态显示为正常

### 2. 在 Cursor 中验证

1. 在 Cursor 中，使用 `@` 符号查看可用的 MCP 工具
2. 或直接询问："列出所有可用的 BrowserTools 工具"
3. 您应该能看到以下工具：
   - `getConsoleLogs` - 获取控制台日志
   - `getConsoleErrors` - 获取控制台错误
   - `getNetworkErrors` - 获取网络错误
   - `getNetworkLogs` - 获取网络日志
   - `screenshot` - 截图
   - `navigate` - 导航到指定URL
   - 等等...

## 使用示例

配置完成后，您可以在 Cursor 中这样使用：

```
请帮我获取当前浏览器页面的截图
```

或者：

```
获取浏览器的控制台日志
```

## 故障排除

### 问题1：连接失败

**解决方案：**
1. 确保 BrowserTools MCP 服务器正在运行
2. 检查端口 3025 是否被占用
3. 验证 Chrome 扩展是否已启用
4. 检查防火墙设置

### 问题2：找不到 MCP 配置选项

**解决方案：**
1. 确保 Cursor 版本是最新的
2. 检查 Cursor 是否支持 MCP 功能
3. 尝试重启 Cursor

### 问题3：MCP 工具不可用

**解决方案：**
1. 检查 Chrome 扩展是否正常运行
2. 验证 MCP 服务器连接状态
3. 查看 Cursor 的日志输出
4. 尝试重新配置 MCP 服务器

## 端口配置

如果端口 3025 已被占用，您可以：

1. **修改 BrowserTools MCP 服务器端口**
   - 设置环境变量：`BROWSER_TOOLS_PORT=3026`
   - 或在启动命令中指定端口

2. **更新 Cursor 配置**
   - 将配置中的端口号改为新的端口号

## 相关资源

- [BrowserTools MCP GitHub](https://github.com/AgentDeskAI/browser-tools-mcp)
- [Cursor MCP 文档](https://docs.cursor.com/zh/cli/mcp)
- [MCP 协议规范](https://modelcontextprotocol.io/)

## 下一步

配置完成后，您可以：
- 使用 BrowserTools 进行网页抓取
- 自动化浏览器操作
- 获取页面截图和日志
- 监控网络请求
- 进行 SEO 分析

---

**注意：** 如果配置过程中遇到问题，请检查：
1. BrowserTools MCP 服务器是否正常运行
2. Chrome 扩展是否正确安装和启用
3. 端口 3025 是否可访问
4. Cursor 版本是否支持 MCP

