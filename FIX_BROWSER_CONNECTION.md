# 修复浏览器连接问题

## 问题：Failed to discover browser connector server

这个错误表示 MCP 工具已加载，但 Chrome 扩展还没有连接到 MCP 服务器。

## 解决步骤

### 步骤 1：确认 MCP 服务器正在运行

检查端口 3025 是否在监听：

```powershell
netstat -ano | findstr :3025
```

如果没有监听，说明 MCP 服务器没有启动。

### 步骤 2：检查 Chrome 扩展

1. **打开 Chrome 浏览器**
   - 访问 `chrome://extensions/`

2. **确认扩展已安装**
   - 查找 "BrowserTools MCP" 或类似的扩展
   - 确保扩展已启用（开关是打开的）

3. **如果未安装，请安装：**
   - 点击右上角的"开发者模式"开关
   - 点击"加载已解压的扩展程序"
   - 选择 BrowserTools MCP 扩展的目录

### 步骤 3：检查扩展连接

1. **打开 Chrome 开发者工具**
   - 按 `F12` 或 `Ctrl+Shift+I`

2. **查找 BrowserToolsMCP 面板**
   - 在开发者工具的标签页中查找
   - 或者查看是否有相关消息

3. **测试连接**
   - 如果看到 "Test connection" 按钮，点击它
   - 查看连接状态

### 步骤 4：重启服务

如果以上都正常，尝试：

1. **完全关闭 Chrome**
   - 关闭所有 Chrome 窗口
   - 确保没有后台进程

2. **重启 Cursor**
   - 完全关闭 Cursor
   - 重新打开 Cursor
   - 这会重启 MCP 服务器

3. **重新打开 Chrome**
   - 打开 Chrome
   - 扩展应该会自动连接到 MCP 服务器

### 步骤 5：检查扩展配置

某些扩展可能需要配置服务器地址：

1. **检查扩展设置**
   - 在 `chrome://extensions/` 中
   - 点击扩展的"详细信息"或"选项"
   - 确认服务器地址是 `localhost:3025` 或 `127.0.0.1:3025`

## 手动启动 MCP 服务器（如果需要）

如果 Cursor 没有自动启动服务器，可以手动启动：

1. **打开终端**
2. **运行启动脚本：**
   ```bash
   D:\Demo\StockAnalyse\start-browser-tools-mcp-wrapper.bat
   ```

   或者直接运行：
   ```bash
   "C:\Program Files\nodejs\npx.cmd" -y @agentdeskai/browser-tools-mcp@latest
   ```

3. **保持终端窗口打开**
   - 服务器需要持续运行

## 验证连接

连接成功后，您应该能看到：

1. **在终端中（如果手动启动）**
   - 显示 "Server listening on port 3025"
   - 或类似的消息

2. **在 Chrome 扩展中**
   - 连接状态显示为"已连接"
   - 或绿色指示

3. **在 Cursor 中使用工具**
   - 不再出现 "Failed to discover" 错误
   - 可以成功获取截图、日志等

## 常见问题

### Q: 扩展显示连接失败
**A:** 检查：
- MCP 服务器是否在运行
- 端口 3025 是否被占用
- 防火墙是否阻止连接

### Q: 找不到扩展
**A:** 需要从以下位置获取：
- GitHub: https://github.com/AgentDeskAI/browser-tools-mcp
- 下载并解压扩展
- 在 Chrome 中加载已解压的扩展

### Q: 端口被占用
**A:** 
- 使用 `netstat -ano | findstr :3025` 查找占用进程
- 结束占用进程，或更改端口配置

## 测试连接

配置完成后，在 Cursor 中尝试：

```
获取浏览器截图
```

如果成功，应该能看到截图。

---

**提示：** 如果问题持续，请检查：
1. Cursor 的 MCP 输出日志
2. Chrome 扩展的控制台错误
3. 系统防火墙设置

