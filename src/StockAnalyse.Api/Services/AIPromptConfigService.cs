using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.IO;
using System;
using System.Threading.Tasks;

namespace StockAnalyse.Api.Services;

public class AIPromptSettings
{
    public string SystemPrompt { get; set; } = "你是一名资深的A股分析师。请结合财务数据、技术指标、消息面、行业地位，对指定股票进行结构化分析，并给出风险提示与操作建议。";
    public double Temperature { get; set; } = 0.7;
}

public class AIPromptConfigService
{
    private readonly string _configFilePath;
    private readonly ILogger<AIPromptConfigService> _logger;

    public AIPromptConfigService(ILogger<AIPromptConfigService> logger)
    {
        _logger = logger;
        _configFilePath = Path.Combine(AppContext.BaseDirectory, "ai-config.json");
    }

    public async Task<AIPromptSettings> GetSettingsAsync()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                var settings = JsonSerializer.Deserialize<AIPromptSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取AI提示词配置失败");
        }

        return new AIPromptSettings();
    }

    public async Task SaveSettingsAsync(AIPromptSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_configFilePath, json);
            _logger.LogInformation("AI提示词配置已保存");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存AI提示词配置失败");
        }
    }
}