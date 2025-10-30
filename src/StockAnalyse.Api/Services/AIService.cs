using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace StockAnalyse.Api.Services;

public class AIService : IAIService
{
    private readonly StockDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AIService> _logger;
    private readonly AIPromptConfigService _promptConfigService;

    public AIService(StockDbContext context, HttpClient httpClient, ILogger<AIService> logger, AIPromptConfigService promptConfigService)
    {
        _context = context;
        _httpClient = httpClient;
        _logger = logger;
        _promptConfigService = promptConfigService;
    }

    public async Task<string> AnalyzeStockAsync(string stockCode, int? promptId = null, string? additionalContext = null)
    {
        var config = await GetActiveAIConfigAsync();
        if (config == null)
        {
            return "请先配置AI模型API";
        }

        var promptText = $"请分析股票代码{stockCode}。{additionalContext ?? ""}";
        var promptSettings = await GetPromptSettingsAsync(promptId);

        var response = await CallAIAsync(config, promptText, promptSettings);
        return response;
    }

    public async Task<string> ChatAsync(string message, string? context = null)
    {
        var config = await GetActiveAIConfigAsync();
        if (config == null)
        {
            return "请先配置AI模型API";
        }
        
        var prompt = context != null ? $"{context}\n\n{message}" : message;
        var promptSettings = await GetPromptSettingsAsync(null); // 使用默认提示词设置
        var response = await CallAIAsync(config, prompt, promptSettings);
        return response;
    }

    public async Task<string> GetStockRecommendationAsync(string stockCode)
    {
        var analysis = await AnalyzeStockAsync(stockCode, null, "请给出买入、持有或卖出的建议，并说明理由。");
        return analysis;
    }

    private async Task<AIModelConfig?> GetActiveAIConfigAsync()
    {
        var config = await _context.AIModelConfigs
            .Where(c => c.IsActive)
            .FirstOrDefaultAsync();
            
        return config ?? await _context.AIModelConfigs
            .Where(c => c.IsDefault)
            .FirstOrDefaultAsync();
    }

    private async Task<AIPromptSettings> GetPromptSettingsAsync(int? promptId)
    {
        // 1) 指定提示词
        if (promptId.HasValue)
        {
            var p = await _context.AIPrompts.FirstOrDefaultAsync(x => x.Id == promptId.Value && x.IsActive);
            if (p != null)
            {
                return new AIPromptSettings { SystemPrompt = p.SystemPrompt, Temperature = p.Temperature };
            }
        }
        // 2) 默认提示词
        var d = await _context.AIPrompts.FirstOrDefaultAsync(x => x.IsDefault && x.IsActive);
        if (d != null)
        {
            return new AIPromptSettings { SystemPrompt = d.SystemPrompt, Temperature = d.Temperature };
        }
        // 3) JSON配置文件回退
        return await _promptConfigService.GetSettingsAsync();
    }

    private async Task<string> CallAIAsync(AIModelConfig config, string userPrompt, AIPromptSettings settings)
    {
        try
        {
            if (config.Name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase))
            {
                return await CallDeepSeekAsync(config, userPrompt, settings);
            }
            else if (config.Name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return await CallOpenAIAsync(config, userPrompt, settings);
            }

            return "不支持的AI模型";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用AI失败");
            return "AI调用失败：" + ex.Message;
        }
    }

    private async Task<string> CallDeepSeekAsync(AIModelConfig config, string userPrompt, AIPromptSettings settings)
    {
        var requestBody = new
        {
            model = config.ModelName ?? "deepseek-chat",
            messages = new[]
            {
                new { role = "system", content = settings.SystemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = settings.Temperature
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/v1/chat/completions") { Content = content };
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        dynamic? result = JsonConvert.DeserializeObject(json);
        return result?.choices[0]?.message?.content?.ToString() ?? "无响应";
    }

    private async Task<string> CallOpenAIAsync(AIModelConfig config, string userPrompt, AIPromptSettings settings)
    {
        var requestBody = new
        {
            model = config.ModelName ?? "gpt-3.5-turbo",
            messages = new[]
            {
                new { role = "system", content = settings.SystemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = settings.Temperature
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions") { Content = content };
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        dynamic? result = JsonConvert.DeserializeObject(json);
        return result?.choices[0]?.message?.content?.ToString() ?? "无响应";
    }
}

