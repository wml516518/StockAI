# 初始化优化选股模板

本目录包含了用于初始化或更新优化选股模板的脚本。

## 📋 优化内容

根据市场行情，已优化"低价成长股"模板的参数：

- **价格**：5-30元
- **换手率**：2%-8%（有一定活跃度但不过度炒作）
- **成交量**：>5000手（保证流动性）
- **市值**：50-500亿元（中小盘成长股）
- **股息率**：0-3%（成长股通常不高）
- **PE**：10-40（合理估值）
- **PB**：1-5（合理市净率）
- **涨跌幅**：-5%到+10%

## 🚀 使用方法

### Windows PowerShell（推荐）

```powershell
# 方法1：直接执行脚本
.\initialize-templates.ps1

# 方法2：或者直接在 PowerShell 中执行命令
Invoke-RestMethod -Uri "http://localhost:5000/api/ScreenTemplate/initialize-optimized" -Method POST -ContentType "application/json"
```

### Windows CMD

双击运行 `initialize-templates.bat` 或在命令行中执行：

```cmd
initialize-templates.bat
```

### Linux/Mac

```bash
# 添加执行权限
chmod +x initialize-templates.sh

# 执行脚本
./initialize-templates.sh
```

### 直接使用 curl（跨平台）

```bash
# Windows (Git Bash 或 WSL)
curl -X POST http://localhost:5000/api/ScreenTemplate/initialize-optimized -H "Content-Type: application/json"

# Linux/Mac
curl -X POST http://localhost:5000/api/ScreenTemplate/initialize-optimized -H "Content-Type: application/json"
```

## ⚙️ 配置

如果 API 运行在不同的端口或地址，请修改脚本中的 `$apiUrl` 变量：

- **默认 HTTP**：`http://localhost:5000`
- **默认 HTTPS**：`https://localhost:5001`

## ✅ 验证

执行成功后，会看到类似以下输出：

```
✅ 模板初始化成功！
   - 已更新: 1 个模板
   - 已创建: 0 个模板
```

然后可以在应用的"条件选股"页面中加载"低价成长股"模板查看优化后的参数。

## ⚠️ 注意事项

1. 执行前请确保 API 服务正在运行
2. 如果遇到权限错误，可能需要以管理员身份运行
3. PowerShell 脚本执行策略限制：如果无法执行，请运行：
   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
   ```

