using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using StockAnalyse.Api.Services.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace StockAnalyse.Api.Services;

public class AIService : IAIService
{
    private readonly StockDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AIService> _logger;
    private readonly AIPromptConfigService _promptConfigService;

    private const string DefaultChatSystemPrompt =
        "你是一位资深投资顾问，服务的对象都是刚入门的理财小白。"
        + "回答要诙谐、有趣、通俗易懂，可适度使用生活化比喻，但不得遗漏关键财务指标、行业信息、风险提示等核心内容。"
        + "用简短段落清晰说明重点，让用户听得懂、记得住。";

    public AIService(StockDbContext context, IHttpClientFactory httpClientFactory, ILogger<AIService> logger, AIPromptConfigService promptConfigService)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _promptConfigService = promptConfigService;
    }
    
    private HttpClient GetHttpClient()
    {
        // 优先使用配置了长超时的HttpClient，如果不存在则使用默认的
        try
        {
            return _httpClientFactory.CreateClient("AIService");
        }
        catch
        {
            // 如果"AIService"客户端不存在，使用默认客户端并设置超时
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            return client;
        }
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

        var promptText = $"帮我分析当前股票{stockCode}。{additionalContext ?? ""}";
        var promptSettings = await GetPromptSettingsAsync(promptId);

        var messages = new List<AiChatMessage>
        {
            new("user", promptText)
        };

        var response = await CallAIAsync(config, messages, promptSettings);
        return response;
    }

    public async Task<string> ExecutePromptAsync(string? promptName, string userPrompt, IDictionary<string, string?>? placeholders = null, int? modelId = null)
    {
        var config = modelId.HasValue 
            ? await _context.AIModelConfigs.FirstOrDefaultAsync(c => c.Id == modelId.Value)
            : await GetActiveAIConfigAsync();

        if (config == null)
        {
            _logger.LogWarning("AI模型尚未配置，无法执行提示词: {PromptName}", promptName ?? "默认提示");
            return "请先配置AI模型API";
        }

        AIPrompt? prompt = null;
        if (!string.IsNullOrWhiteSpace(promptName))
        {
            prompt = await _context.AIPrompts.FirstOrDefaultAsync(p => p.Name == promptName && p.IsActive);
            if (prompt == null)
            {
                _logger.LogWarning("未找到名称为 {PromptName} 的提示词，使用默认提示词设置", promptName);
            }
        }

        AIPromptSettings settings;
        if (prompt != null)
        {
            settings = new AIPromptSettings
            {
                SystemPrompt = ApplyPlaceholders(prompt.SystemPrompt ?? string.Empty, placeholders),
                Temperature = prompt.Temperature
            };
        }
        else
        {
            var fallbackSettings = await GetPromptSettingsAsync(null);
            settings = new AIPromptSettings
            {
                SystemPrompt = ApplyPlaceholders(fallbackSettings.SystemPrompt, placeholders),
                Temperature = fallbackSettings.Temperature
            };
        }

        var finalUserPrompt = ApplyPlaceholders(userPrompt ?? string.Empty, placeholders);

        _logger.LogInformation("执行AI提示词: PromptName={PromptName}, 用户提示长度={Length}", promptName ?? "默认提示", finalUserPrompt.Length);

        var messages = new List<AiChatMessage>
        {
            new("user", finalUserPrompt)
        };

        return await CallAIAsync(config, messages, settings);
    }

    public async Task<string> ChatAsync(IEnumerable<AiChatMessage> messages, string? context = null, int? modelId = null, int maxHistory = 5)
    {
        var config = modelId.HasValue
            ? await _context.AIModelConfigs.FirstOrDefaultAsync(c => c.Id == modelId.Value)
            : await GetActiveAIConfigAsync();
        if (config == null)
        {
            return "请先配置AI模型API";
        }

        var promptSettings = await GetPromptSettingsAsync(null); // 默认提示词设置
        if (string.IsNullOrWhiteSpace(promptSettings.SystemPrompt))
        {
            promptSettings.SystemPrompt = DefaultChatSystemPrompt;
        }

        var conversation = new List<AiChatMessage>();
        if (!string.IsNullOrWhiteSpace(context))
        {
            conversation.Add(new AiChatMessage("system", context));
        }

        if (messages != null)
        {
            var history = messages
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Content))
                .ToList();

            var historyLimit = Math.Max(1, Math.Min(maxHistory, 10)) * 2;
            if (history.Count > historyLimit)
            {
                history = history.Skip(history.Count - historyLimit).ToList();
            }

            conversation.AddRange(history);
        }

        if (!conversation.Any())
        {
            return "请提供至少一条对话消息";
        }

        var response = await CallAIAsync(config, conversation, promptSettings);
        return response;
    }

    private async Task<string> CallAIAsync(AIModelConfig config, IEnumerable<AiChatMessage> messages, AIPromptSettings settings)
    {
        try
        {
            // 检查配置是否完整
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                return "AI配置错误: API密钥未设置";
            }

            var conversation = new List<AiChatMessage>();
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
            {
                conversation.Add(new AiChatMessage("system", settings.SystemPrompt));
            }

            if (messages != null)
            {
                conversation.AddRange(messages);
            }

            if (!conversation.Any())
            {
                return "AI对话消息为空，请提供有效的消息内容";
            }

            if (config.Name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase))
            {
                return await CallDeepSeekAsync(config, conversation, settings.Temperature);
            }
            else if (config.Name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return await CallOpenAIAsync(config, conversation, settings.Temperature);
            }
            else if (!string.IsNullOrEmpty(config.SubscribeEndpoint))
            {
                // 尝试通用调用方式
                if (config.SubscribeEndpoint.Contains("openai", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallOpenAIAsync(config, conversation, settings.Temperature);
                }
                else if (config.SubscribeEndpoint.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallDeepSeekAsync(config, conversation, settings.Temperature);
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

    private static string ApplyPlaceholders(string? template, IDictionary<string, string?>? placeholders)
    {
        if (string.IsNullOrEmpty(template) || placeholders == null || placeholders.Count == 0)
        {
            return template ?? string.Empty;
        }

        var result = template;
        foreach (var kvp in placeholders)
        {
            if (string.IsNullOrEmpty(kvp.Key))
            {
                continue;
            }
            result = result.Replace(kvp.Key, kvp.Value ?? string.Empty);
        }

        return result;
    }

    private async Task<string> CallDeepSeekAsync(AIModelConfig config, IEnumerable<AiChatMessage> messages, double? temperature)
    {
        var messageArray = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        var requestBody = new
        {
            model = config.ModelName ?? "deepseek-chat",
            messages = messageArray,
            temperature = temperature
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, config.SubscribeEndpoint ?? "https://api.deepseek.com/v1/chat/completions")
        {
            Content = content
        };
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

        using var httpClient = GetHttpClient();
        var response = await httpClient.SendAsync(request);
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
                if (result?.choices != null)
                {
                    JArray? choicesArray = null;
                    if (result.choices is JArray directArray)
                    {
                        choicesArray = directArray;
                    }
                    else if (result.choices is JToken choicesToken && choicesToken.Type == JTokenType.Array)
                    {
                        choicesArray = choicesToken as JArray;
                    }
                    else
                    {
                        var fallbackToken = result.choices as JToken;
                        if (fallbackToken?.Type == JTokenType.Array)
                        {
                            choicesArray = fallbackToken as JArray;
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

    private async Task<string> CallOpenAIAsync(AIModelConfig config, IEnumerable<AiChatMessage> messages, double? temperature)
    {
        var messageArray = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        var requestBody = new
        {
            model = config.ModelName ?? "gpt-3.5-turbo",
            messages = messageArray,
            temperature = temperature
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, config.SubscribeEndpoint ?? "https://api.openai.com/v1/chat/completions")
        {
            Content = content
        };
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

        using var httpClient = GetHttpClient();
        var response = await httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        try
        {
            dynamic? result = JsonConvert.DeserializeObject(json);

            if (result?.error != null)
            {
                string errorMessage = result?.error?.message?.ToString() ?? "未知错误";
                _logger.LogError("OpenAI API返回错误: {ErrorMessage}", errorMessage);
                return $"AI调用失败: {errorMessage}";
            }

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

            string? messageContent = null;
            try
            {
                if (result?.choices != null)
                {
                    JArray? choicesArray = null;
                    if (result.choices is JArray directArray)
                    {
                        choicesArray = directArray;
                    }
                    else if (result.choices is JToken choicesToken && choicesToken.Type == JTokenType.Array)
                    {
                        choicesArray = choicesToken as JArray;
                    }
                    else
                    {
                        var fallbackToken = result.choices as JToken;
                        if (fallbackToken?.Type == JTokenType.Array)
                        {
                            choicesArray = fallbackToken as JArray;
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
}

