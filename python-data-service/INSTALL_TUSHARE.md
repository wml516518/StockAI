# TuShare 库安装指南

## 问题
Python服务日志显示：`⚠️ [TuShare] 未安装tushare库，跳过`

## 解决方案

### 方法1：使用pip安装（推荐）

打开命令提示符（CMD），运行：

```bash
pip install tushare
```

或者（如果上面的命令失败）：

```bash
py -m pip install tushare
```

或者：

```bash
python -m pip install tushare
```

### 方法2：使用requirements.txt安装

在 `python-data-service` 目录下运行：

```bash
cd e:\TraeDemo\StockAI\python-data-service
pip install -r requirements.txt
```

### 方法3：手动下载安装

如果网络问题导致pip安装失败：

1. 访问 https://pypi.org/project/tushare/#files
2. 下载 `.whl` 文件
3. 运行：`pip install 下载的文件名.whl`

## 验证安装

安装完成后，在Python中测试：

```python
python
>>> import tushare as ts
>>> print(ts.__version__)
```

如果没有报错，说明安装成功。

## 重启服务

安装完成后，重启Python服务：

1. 关闭当前的Python服务窗口
2. 重新运行 `.\start-all-services.bat`

## 查看日志

重启后查看Python服务日志，应该看到：
- `✅ TuShare Token已配置`
- 不再出现 `⚠️ [TuShare] 未安装tushare库`

## 注意事项

- TuShare需要Python 3.7+
- 安装可能需要几分钟时间
- 如果遇到权限问题，尝试以管理员身份运行CMD
