# BrowserTools MCP 故障排除指南

## 错误：No server info found

如果您看到错误 `@anysphere.cursor-mcp.MCP user-browser-tools (39) No server info found`，请尝试以下解决方案：

### 解决方案 1：检查 Node.js 和 npx

确保 Node.js 和 npx 可用：

```bash
node --version
npx --version
```

如果 npx 不可用，可能需要更新 Node.js。

### 解决方案 2：尝试使用完整路径

如果 npx 有问题，尝试使用完整路径：

```json
{
  "mcpServers": {
    "browser-tools": {
      "command": "C:\\Program Files\\nodejs\\npx.cmd",
      "args": [
        "-y",
        "@agentdeskai/browser-tools-mcp@latest"
      ],
      "env": {
        "BROWSER_TOOLS_HOST": "127.0.0.1",
        "BROWSER_TOOLS_PORT": "3025"
      }
    }
  }
}
```

### 解决方案 3：检查端口冲突

确保端口 3025 可用，或者使用其他端口：

```json
{
  "mcpServers": {
    "browser-tools": {
      "command": "npx",
      "args": [
        "-y",
        "@agentdeskai/browser-tools-mcp@latest"
      ],
      "env": {
        "BROWSER_TOOLS_HOST": "127.0.0.1",
        "BROWSER_TOOLS_PORT": "3026"
      }
    }
  }
}
```

### 解决方案 4：手动安装 BrowserTools MCP

先全局安装，然后使用命令路径：

```bash
npm install -g @agentdeskai/browser-tools-mcp
```

然后配置：

```json
{
  "mcpServers": {
    "browser-tools": {
      "command": "browser-tools-mcp",
      "env": {
        "BROWSER_TOOLS_HOST": "127.0.0.1",
        "BROWSER_TOOLS_PORT": "3025"
      }
    }
  }
}
```

### 解决方案 5：检查 Chrome 扩展

确保 Chrome 扩展已正确安装：

1. 打开 Chrome，访问 `chrome://extensions/`
2. 启用"开发者模式"
3. 确认 BrowserTools MCP 扩展已启用
4. 检查扩展的 ID 和状态

### 解决方案 6：检查 Cursor 日志

查看 Cursor 的输出面板，寻找更多错误信息：

1. 在 Cursor 中，打开"输出"面板
2. 选择 "MCP" 或 "anysphere.cursor-mcp" 输出通道
3. 查看详细的错误信息

### 解决方案 7：重启服务

1. 完全关闭 Cursor
2. 停止所有 BrowserTools MCP 服务器进程
3. 重新打开 Cursor
4. 让 Cursor 自动启动 MCP 服务器

### 解决方案 8：检查防火墙

确保防火墙没有阻止 localhost:3025 的连接。

## 当前配置

您当前的配置文件位于：`c:\Users\11872\.cursor\mcp.json`

配置内容：
```json
{
  "mcpServers": {
    "browser-tools": {
      "command": "npx",
      "args": [
        "-y",
        "@agentdeskai/browser-tools-mcp@latest"
      ],
      "env": {
        "BROWSER_TOOLS_HOST": "127.0.0.1",
        "BROWSER_TOOLS_PORT": "3025"
      }
    }
  }
}
```

## 验证步骤

1. **检查配置文件格式**：确保 JSON 格式正确，没有语法错误
2. **重启 Cursor**：完全退出并重新打开 Cursor
3. **查看 MCP 状态**：在 Cursor 中查看 MCP 服务器状态
4. **检查日志**：查看 Cursor 的输出日志

## 需要帮助？

如果以上方法都无法解决问题，请提供：
- Cursor 版本号
- Node.js 版本号
- 完整的错误日志
- 配置文件内容

这样可以更准确地诊断问题。

