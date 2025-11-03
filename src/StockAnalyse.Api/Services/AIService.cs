using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Linq;

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

    public async Task<string> AnalyzeStockAsync(string stockCode, int? promptId = null, string? additionalContext = null, int? modelId = null)
    {
        var config = modelId.HasValue 
            ? await _context.AIModelConfigs.FirstOrDefaultAsync(c => c.Id == modelId.Value)
            : await GetActiveAIConfigAsync();
            
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
            // 检查配置是否完整
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                return "AI配置错误: API密钥未设置";
            }
            
            if (config.Name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase))
            {
                return await CallDeepSeekAsync(config, userPrompt, settings);
            }
            else if (config.Name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return await CallOpenAIAsync(config, userPrompt, settings);
            }
            else if (!string.IsNullOrEmpty(config.SubscribeEndpoint))
            {
                // 尝试通用调用方式
                if (config.SubscribeEndpoint.Contains("openai", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallOpenAIAsync(config, userPrompt, settings);
                }
                else if (config.SubscribeEndpoint.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallDeepSeekAsync(config, userPrompt, settings);
                }
            }

            return "不支持的AI模型或配置不完整，请检查模型名称和API端点";
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
        var request = new HttpRequestMessage(HttpMethod.Post, config.SubscribeEndpoint ?? "https://api.deepseek.com/v1/chat/completions") { Content = content };
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
    
        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
    
        try
        {
            dynamic? result = JsonConvert.DeserializeObject(json);
            
            // 检查API是否返回错误
            if (result?.error != null)
            {
                string errorMessage = result?.error?.message?.ToString() ?? "未知错误";
                _logger.LogError("DeepSeek API返回错误: {ErrorMessage}", errorMessage);
                return $"AI调用失败: {errorMessage}";
            }
            
            // 检查返回结构是否符合预期
            bool hasChoices = false;
            if (result?.choices != null)
            {
                if (result.choices is JArray jArray && jArray.Count > 0)
                {
                    hasChoices = true;
                }
                else if (result.choices is JToken jToken && jToken.Type == JTokenType.Array && jToken.Count() > 0)
                {
                    hasChoices = true;
                }
                else
                {
                    // 尝试动态访问Count属性
                    try
                    {
                        dynamic choices = result.choices;
                        if (choices != null && choices.Count > 0)
                        {
                            hasChoices = true;
                        }
                    }
                    catch
                    {
                        // 忽略转换错误
                    }
                }
            }
            
            if (!hasChoices)
            {
                _logger.LogError("DeepSeek API返回结构异常: {Json}", json);
                return "AI返回结构异常，请检查API配置";
            }
            
            // 安全访问内容
            string? messageContent = null;
            try
            {
                // JArray使用Count属性，而不是Length
                if (result?.choices != null)
                {
                    // 将choices转换为JArray以便安全访问
                    JArray? choicesArray = null;
                    if (result.choices is JArray jArray)
                    {
                        choicesArray = jArray;
                    }
                    else if (result.choices is JToken jToken && jToken.Type == JTokenType.Array)
                    {
                        choicesArray = jToken as JArray;
                    }
                    else
                    {
                        // 尝试将dynamic转换为JArray
                        var token = result.choices as JToken;
                        if (token?.Type == JTokenType.Array)
                        {
                            choicesArray = token as JArray;
                        }
                    }
                    
                    if (choicesArray != null && choicesArray.Count > 0)
                    {
                        var firstChoice = choicesArray[0];
                        if (firstChoice != null)
                        {
                            var messageToken = firstChoice["message"];
                            if (messageToken != null)
                            {
                                var contentToken = messageToken["content"];
                                if (contentToken != null)
                                {
                                    messageContent = contentToken.ToString();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析DeepSeek API响应内容失败");
                return "解析AI响应失败: " + ex.Message;
            }
            
            return messageContent ?? "无响应";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析DeepSeek API响应失败: {Json}", json);
            return "解析AI响应失败: " + ex.Message;
        }
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
        var request = new HttpRequestMessage(HttpMethod.Post, config.SubscribeEndpoint ?? "https://api.openai.com/v1/chat/completions") { Content = content };
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
    
        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
    
        try
        {
            dynamic? result = JsonConvert.DeserializeObject(json);
            
            // 检查API是否返回错误
            if (result?.error != null)
            {
                string errorMessage = result?.error?.message?.ToString() ?? "未知错误";
                _logger.LogError("OpenAI API返回错误: {ErrorMessage}", errorMessage);
                return $"AI调用失败: {errorMessage}";
            }
            
            // 检查返回结构是否符合预期
            bool hasChoices = false;
            if (result?.choices != null)
            {
                if (result.choices is JArray jArray && jArray.Count > 0)
                {
                    hasChoices = true;
                }
                else if (result.choices is JToken jToken && jToken.Type == JTokenType.Array && jToken.Count() > 0)
                {
                    hasChoices = true;
                }
                else
                {
                    // 尝试动态访问Count属性
                    try
                    {
                        dynamic choices = result.choices;
                        if (choices != null && choices.Count > 0)
                        {
                            hasChoices = true;
                        }
                    }
                    catch
                    {
                        // 忽略转换错误
                    }
                }
            }
            
            if (!hasChoices)
            {
                _logger.LogError("OpenAI API返回结构异常: {Json}", json);
                return "AI返回结构异常，请检查API配置";
            }
            
            // 安全访问内容
            string? messageContent = null;
            try
            {
                if (result?.choices != null)
                {
                    // 将choices转换为JArray以便安全访问
                    JArray? choicesArray = null;
                    if (result.choices is JArray jArray)
                    {
                        choicesArray = jArray;
                    }
                    else if (result.choices is JToken jToken && jToken.Type == JTokenType.Array)
                    {
                        choicesArray = jToken as JArray;
                    }
                    else
                    {
                        // 尝试将dynamic转换为JArray
                        var token = result.choices as JToken;
                        if (token?.Type == JTokenType.Array)
                        {
                            choicesArray = token as JArray;
                        }
                    }
                    
                    if (choicesArray != null && choicesArray.Count > 0)
                    {
                        var firstChoice = choicesArray[0];
                        if (firstChoice != null)
                        {
                            var messageToken = firstChoice["message"];
                            if (messageToken != null)
                            {
                                var contentToken = messageToken["content"];
                                if (contentToken != null)
                                {
                                    messageContent = contentToken.ToString();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析OpenAI API响应内容失败");
                return "解析AI响应失败: " + ex.Message;
            }
            
            return messageContent ?? "无响应";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析OpenAI API响应失败: {Json}", json);
            return "解析AI响应失败: " + ex.Message;
        }
    }
}

