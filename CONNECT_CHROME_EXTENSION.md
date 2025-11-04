# 连接 Chrome 扩展到 MCP 服务器

## 当前状态

✅ **MCP 服务器：** 正在运行（端口 3025）
❌ **Chrome 扩展：** 未连接

错误信息：`Chrome extension not connected`

## 解决步骤

### 步骤 1：检查 Chrome 扩展是否已安装

1. 打开 Chrome 浏览器
2. 访问 `chrome://extensions/`
3. 查找 "BrowserTools MCP" 或类似的扩展
4. 确保扩展已启用（开关是打开的）

### 步骤 2：如果扩展未安装

1. **下载扩展：**
   - 访问：https://github.com/AgentDeskAI/browser-tools-mcp
   - 下载最新版本
   - 解压文件

2. **加载扩展：**
   - 在 `chrome://extensions/` 页面
   - 启用右上角的"开发者模式"
   - 点击"加载已解压的扩展程序"
   - 选择解压后的 `chrome-extension` 目录

### 步骤 3：检查扩展连接

1. **打开 Chrome 开发者工具**
   - 按 `F12` 或 `Ctrl+Shift+I`

2. **查找 BrowserToolsMCP 面板**
   - 在开发者工具的标签页中查找
   - 或者查看 Console 标签中的消息

3. **测试连接**
   - 如果看到扩展面板，点击 "Test connection" 或 "Connect"
   - 查看连接状态

### 步骤 4：检查扩展配置

某些扩展可能需要配置服务器地址：

1. **在扩展详情中查找设置**
   - 点击扩展的"详细信息"或"选项"
   - 确认服务器地址是：
     - `localhost:3025`
     - 或 `127.0.0.1:3025`

### 步骤 5：重启 Chrome

1. **完全关闭 Chrome**
   - 关闭所有 Chrome 窗口
   - 在任务管理器中确保没有 Chrome 进程

2. **重新打开 Chrome**
   - 扩展应该会自动尝试连接

3. **检查连接状态**
   - 再次尝试获取截图

## 验证连接

连接成功后：

1. **在扩展中应该看到**
   - 连接状态显示为"已连接"
   - 或绿色指示

2. **在 Cursor 中使用工具**
   - 不再出现 "Chrome extension not connected" 错误
   - 可以成功获取截图

## 常见问题

### Q: 找不到扩展
**A:** 
- 确保从正确的 GitHub 仓库下载
- 仓库：https://github.com/AgentDeskAI/browser-tools-mcp
- 下载 Releases 中的最新版本

### Q: 扩展安装了但不连接
**A:** 检查：
- 服务器是否在运行（端口 3025）
- 扩展配置中的服务器地址是否正确
- 防火墙是否阻止了连接

### Q: 扩展显示连接失败
**A:** 
- 确认 MCP 服务器正在运行
- 检查扩展的 Console 日志（在扩展详情页点击"检查视图"）

## 测试

配置完成后，在 Cursor 中再次尝试：

```
获取浏览器截图
```

如果成功，应该能看到截图。

