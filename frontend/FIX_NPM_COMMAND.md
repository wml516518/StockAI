# 修复 npm 命令无法识别的问题

## 问题说明

在新打开的 PowerShell 窗口中，运行 `npm` 命令时提示"无法识别"，这是因为新窗口还没有加载更新后的环境变量。

## 快速解决方案

### 方法一：刷新环境变量（推荐）

在 PowerShell 中运行以下命令：

```powershell
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
```

然后验证：

```powershell
node --version
npm --version
```

### 方法二：使用刷新脚本

运行项目根目录下的脚本：

```powershell
.\frontend\refresh-env.ps1
```

### 方法三：重新打开 PowerShell

关闭当前 PowerShell 窗口，重新打开一个新的 PowerShell 窗口。新窗口会自动加载最新的环境变量。

## 永久解决方案

### 添加到 PowerShell 配置文件

如果您经常遇到这个问题，可以将刷新命令添加到 PowerShell 配置文件中：

1. **创建或编辑配置文件**：
   ```powershell
   if (!(Test-Path $PROFILE)) {
       New-Item -ItemType File -Path $PROFILE -Force
   }
   notepad $PROFILE
   ```

2. **添加以下内容**：
   ```powershell
   # 自动刷新环境变量
   $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
   ```

3. **保存并重新打开 PowerShell**

## 验证安装

运行以下命令验证 Node.js 和 npm 是否可用：

```powershell
node --version
npm --version
```

如果显示版本号，说明配置成功。

## 启动开发服务器

环境变量刷新后，可以正常使用 npm 命令：

```powershell
cd frontend
npm run dev
```

## 常见问题

### Q: 为什么新窗口找不到 npm？

**A:** 因为新打开的 PowerShell 窗口会重新加载环境变量，而 Node.js 安装程序更新的是系统环境变量，需要重新加载才能生效。

### Q: 每次都要刷新吗？

**A:** 如果添加到 PowerShell 配置文件，每次打开新窗口时都会自动刷新。或者您可以直接重新打开 PowerShell 窗口，新窗口会自动加载最新的环境变量。

### Q: 有没有一键刷新的方法？

**A:** 可以创建一个 PowerShell 别名：

```powershell
function Refresh-Env {
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
    Write-Host "环境变量已刷新！" -ForegroundColor Green
}

Set-Alias -Name refresh -Value Refresh-Env
```

然后每次只需要运行 `refresh` 即可。

## 下一步

环境变量刷新后，请按照 `QUICKSTART.md` 中的说明继续操作。

