# Python检测问题修复指南

## 🔍 问题说明

如果启动脚本提示"未检测到Python"，通常是因为：
1. Python未安装
2. Python未添加到PATH环境变量
3. 环境变量未刷新

## ✅ 解决方案

### 方案1：安装Python并添加到PATH（推荐）

1. **下载Python**
   - 访问：https://www.python.org/downloads/
   - 下载Python 3.8或更高版本

2. **安装时重要步骤**
   - ✅ **必须勾选** "Add Python to PATH" 选项
   - ✅ 选择 "Install Now" 或 "Customize installation"
   - ✅ 安装完成后重启命令行窗口

3. **验证安装**
   ```powershell
   python --version
   # 应该显示：Python 3.x.x
   ```

### 方案2：手动添加到PATH

如果Python已安装但未添加到PATH：

1. **找到Python安装路径**
   - 常见路径：
     - `C:\Users\你的用户名\AppData\Local\Programs\Python\Python312\`
     - `C:\Python312\`
     - `C:\Program Files\Python312\`

2. **添加到PATH**
   - 右键"此电脑" → "属性"
   - "高级系统设置" → "环境变量"
   - 在"系统变量"中找到"Path"，点击"编辑"
   - 添加Python路径和Scripts路径：
     - `C:\Users\你的用户名\AppData\Local\Programs\Python\Python312\`
     - `C:\Users\你的用户名\AppData\Local\Programs\Python\Python312\Scripts\`
   - 确定保存

3. **刷新环境变量**
   - 关闭所有命令行窗口
   - 重新打开PowerShell或CMD
   - 或运行：`refreshenv`（如果安装了Chocolatey）

### 方案3：使用Python Launcher（py命令）

Windows 10/11通常自带Python Launcher：

```powershell
# 测试py命令
py --version

# 如果可用，可以直接使用
py stock_data_service.py
```

### 方案4：使用完整路径启动

如果Python已安装但检测不到，可以直接使用完整路径：

```powershell
# 找到Python.exe的完整路径，例如：
C:\Users\你的用户名\AppData\Local\Programs\Python\Python312\python.exe stock_data_service.py
```

## 🔧 改进的检测逻辑

启动脚本现在会自动尝试多种方式检测Python：

1. **命令检测**：`python`、`py`、`python3`
2. **路径检测**：常见安装路径
3. **版本验证**：确保Python 3.8+

### 支持的检测路径

- `%LOCALAPPDATA%\Programs\Python\Python3xx\`
- `C:\Python3xx\`
- `C:\Program Files\Python3xx\`

## 🚀 快速测试

运行以下命令测试Python是否可用：

```powershell
# 测试命令检测
python --version
py --version
python3 --version

# 测试路径检测（替换为你的实际路径）
& "C:\Users\你的用户名\AppData\Local\Programs\Python\Python312\python.exe" --version
```

## 📝 常见问题

### Q: 为什么安装Python后还是检测不到？

A: 可能是因为：
- 未勾选"Add Python to PATH"
- 环境变量未刷新（需要重启命令行）
- Python安装路径不在常见位置

**解决方法**：手动添加到PATH或使用完整路径

### Q: 可以使用Python 2.x吗？

A: 不可以。本项目需要Python 3.8或更高版本。

### Q: 如何检查Python版本？

A: 运行 `python --version` 或 `py --version`

### Q: 安装了多个Python版本怎么办？

A: 脚本会自动选择第一个检测到的Python 3.8+版本。你也可以手动指定：

```powershell
# 使用特定版本的Python
py -3.11 stock_data_service.py
```

## 💡 提示

- 安装Python时**务必勾选**"Add Python to PATH"
- 修改PATH后需要**重启命令行窗口**
- 如果仍有问题，可以手动运行服务：
  ```powershell
  cd python-data-service
  python stock_data_service.py
  ```

## 🔗 相关文档

- [Python官方下载](https://www.python.org/downloads/)
- [Python PATH配置指南](https://docs.python.org/3/using/windows.html#configuring-python)
- [项目启动指南](../README.md)

