# TuShare Token 配置说明

## 如何获取TuShare Token

1. 访问 TuShare官网: https://tushare.pro/register
2. 注册账号（免费）
3. 登录后，在个人中心找到你的Token
4. 复制Token

## 如何配置

打开 `config.ini` 文件，找到 `[TuShare]` 部分：

```ini
[TuShare]
# TuShare API Token (从 https://tushare.pro/register 注册获取)
Token = 你的token粘贴在这里
```

## 示例

```ini
[TuShare]
Token = 1234567890abcdef1234567890abcdef1234567890abcdef
```

## 验证配置

重启Python服务后，查看启动日志：
- 如果看到 `✅ TuShare Token已配置`，说明配置成功
- 如果看到 `⚠️ TuShare Token未配置（留空）`，说明需要填写Token

## 注意事项

1. Token是免费的，但有调用次数限制
2. 不要将Token分享给他人
3. 如果不配置Token，系统会跳过TuShare数据源，使用其他数据源
