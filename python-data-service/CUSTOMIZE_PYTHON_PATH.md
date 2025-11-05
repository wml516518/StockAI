# 自定义Python路径配置指南

如果你的Python安装在非标准位置，或者自动检测失败，可以通过配置文件手动指定Python路径。

## 🔧 配置方法

### 步骤1: 编辑配置文件

打开文件：`python-data-service\python_path.conf`

### 步骤2: 取消注释并填入路径

取消注释 `PYTHON_CMD=` 行，填入你的Python路径：

**方式1: 使用Python命令名（推荐，如果Python在PATH中）**
```conf
PYTHON_CMD=python
```
或
```conf
PYTHON_CMD=py
```

**方式2: 使用完整路径**
```conf
PYTHON_CMD=C:\Users\你的用户名\AppData\Local\Programs\Python\Python312\python.exe
```

或使用正斜杠：
```conf
PYTHON_CMD=C:/Users/你的用户名/AppData/Local/Programs/Python/Python312/python.exe
```

或使用双反斜杠：
```conf
PYTHON_CMD=C:\\Users\\你的用户名\\AppData\\Local\\Programs\\Python\\Python312\\python.exe
```

**方式3: 相对路径（如果配置文件和Python在同一驱动器）**
```conf
PYTHON_CMD=D:\Python\Python312\python.exe
```

### 步骤3: 保存文件

保存 `python_path.conf` 文件。

### 步骤4: 重新运行启动脚本

运行 `start-all-services.bat`，脚本会自动读取配置文件中的路径。

## 📝 配置示例

### 示例1: Python安装在D盘
```conf
PYTHON_CMD=D:\Python\Python312\python.exe
```

### 示例2: Python安装在E盘的Programs目录
```conf
PYTHON_CMD=E:\Programs\Python\Python311\python.exe
```

### 示例3: Python安装在自定义用户目录
```conf
PYTHON_CMD=C:\Users\张三\MyPython\Python310\python.exe
```

### 示例4: 使用Python命令（如果已添加到PATH）
```conf
PYTHON_CMD=python
```

## 🔍 如何找到Python路径

### 方法1: 在命令行中查找
```cmd
where python
```
或
```cmd
where py
```

### 方法2: 在文件资源管理器中查找
1. 打开文件资源管理器
2. 搜索 `python.exe`
3. 右键点击 `python.exe` → 属性 → 查看"位置"路径

### 方法3: 通过Python自身查找
```cmd
python -c "import sys; print(sys.executable)"
```

## ✅ 验证配置

配置完成后，运行启动脚本，你应该看到：
```
[1/3] 启动Python数据服务...
  检查Python环境...
  从配置文件读取Python路径: D:\Python\Python312\python.exe
  检测到Python: Python 3.12.x
  使用Python命令: D:\Python\Python312\python.exe
```

## ❌ 常见问题

### Q: 配置后仍然检测不到？
A: 检查以下几点：
1. 配置文件路径是否正确：`python-data-service\python_path.conf`
2. 是否取消了注释（删除了 `#` 号）
3. 路径是否正确（可以使用引号，但通常不需要）
4. Python可执行文件是否存在

### Q: 路径中包含空格怎么办？
A: 不需要特殊处理，脚本会自动处理。例如：
```conf
PYTHON_CMD=C:\Program Files\Python312\python.exe
```

### Q: 可以使用相对路径吗？
A: 可以，但建议使用绝对路径，更可靠。

### Q: 如何取消配置？
A: 注释掉 `PYTHON_CMD=` 行（在前面加 `#`），或删除这行。

## 💡 提示

- 配置文件中的路径优先级最高，会优先于自动检测
- 如果配置文件中指定了路径但文件不存在，会继续尝试自动检测
- 建议在配置文件中使用绝对路径，避免因工作目录变化导致问题
- 如果有多台电脑，可以为每台电脑配置不同的路径

## 🔗 相关文档

- [Python检测问题修复指南](PYTHON_DETECTION_FIX.md)
- [AKShare与AI功能集成说明](AKSHARE_AI_INTEGRATION.md)

