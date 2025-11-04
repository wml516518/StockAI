# Node.js 安装指南

## 问题说明

运行 `npm install` 时提示无法识别 `npm` 命令，这是因为系统上没有安装 Node.js。

## 解决方案

### 方法一：从官网下载安装（推荐）

1. **访问 Node.js 官网**
   - 打开浏览器访问：https://nodejs.org/
   - 或者直接下载 LTS 版本：https://nodejs.org/dist/v20.11.0/node-v20.11.0-x64.msi

2. **下载并安装**
   - 下载 Windows 安装包（.msi 文件）
   - 双击运行安装程序
   - 按照向导完成安装（保持默认选项即可）
   - **重要**：安装过程中确保勾选 "Add to PATH" 选项

3. **验证安装**
   - 安装完成后，**关闭并重新打开 PowerShell**
   - 运行以下命令验证：
     ```powershell
     node --version
     npm --version
     ```
   - 如果显示版本号，说明安装成功

### 方法二：使用 Chocolatey（如果已安装）

如果您的系统上已安装 Chocolatey 包管理器，可以使用：

```powershell
choco install nodejs
```

### 方法三：使用 winget（Windows 10/11）

```powershell
winget install OpenJS.NodeJS.LTS
```

## 安装后步骤

1. **重新打开 PowerShell**
   - 关闭当前的 PowerShell 窗口
   - 重新打开一个新的 PowerShell 窗口

2. **验证安装**
   ```powershell
   node --version
   npm --version
   ```

3. **进入前端目录并安装依赖**
   ```powershell
   cd D:\Demo\StockAnalyse\frontend
   npm install
   ```

4. **启动开发服务器**
   ```powershell
   npm run dev
   ```

## 版本要求

- **Node.js**: 16.0 或更高版本（推荐使用 LTS 版本）
- **npm**: 随 Node.js 自动安装，通常版本 >= 8.0

## 常见问题

### Q: 安装后仍然提示找不到命令

**A:** 请尝试以下步骤：
1. 确保安装时选择了 "Add to PATH"
2. 完全关闭并重新打开 PowerShell（不只是新标签页）
3. 检查环境变量：
   - 右键"此电脑" → "属性" → "高级系统设置" → "环境变量"
   - 确认 `Path` 变量中包含 Node.js 的安装路径（通常是 `C:\Program Files\nodejs\`）

### Q: 如何检查 PATH 环境变量？

**A:** 在 PowerShell 中运行：
```powershell
$env:Path -split ';' | Select-String nodejs
```

如果没有任何输出，说明 Node.js 没有添加到 PATH。

### Q: 手动添加到 PATH

**A:** 如果 Node.js 已安装但 PATH 中没有：
1. 找到 Node.js 安装目录（通常是 `C:\Program Files\nodejs\`）
2. 复制该路径
3. 添加到系统环境变量 PATH 中
4. 重新打开 PowerShell

## 快速安装脚本

如果您已经安装了 Node.js，但 PATH 没有正确配置，可以尝试：

```powershell
# 检查 Node.js 是否存在于常见位置
$nodePaths = @(
    "C:\Program Files\nodejs\node.exe",
    "C:\Program Files (x86)\nodejs\node.exe",
    "$env:ProgramFiles\nodejs\node.exe"
)

foreach ($path in $nodePaths) {
    if (Test-Path $path) {
        Write-Host "找到 Node.js: $path"
        $env:Path += ";$((Get-Item $path).DirectoryName)"
        break
    }
}
```

## 下一步

安装完成后，请按照 `QUICKSTART.md` 中的说明继续操作。

