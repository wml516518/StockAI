using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using StockAnalyse.Api.Services.Abstractions;
using StockAnalyse.Api.Services.AITools;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StockAnalyse.Api.Services;

#region Tool Definitions
public class AiTool
{
    [JsonProperty("type")]
    public string Type { get; set; } = "function";

    [JsonProperty("function")]
    public AiFunction Function { get; set; } = new();
}

public class AiFunction
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("parameters")]
    public object Parameters { get; set; } = new { };
}

public class LlmResponse
{
    public string? Content { get; set; }
    public List<AiToolCall>? ToolCalls { get; set; }
    public string? FinishReason { get; set; }
}
#endregion

public class AIPromptSettings
{
    public string SystemPrompt { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
}

public class AIService : IAIService
{
    private readonly StockDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AIService> _logger;
    private readonly IStockDataService _stockDataService;
    private readonly INewsService _newsService;
    private readonly IIndustryService _industryService;
    private readonly IMarketService _marketService;
    private readonly IAIToolFactory _toolFactory;

    private const string DefaultChatSystemPrompt =
        "你是一位资深投资顾问，服务的对象都是刚入门的理财小白。"
        + "回答要诙谐、有趣、通俗易懂，可适度使用生活化比喻，但不得遗漏关键财务指标、行业信息、风险提示等核心内容,且确保数据的实时性和准确性。"
        + "用简短段落清晰说明重点，让用户听得懂、记得住。";

    private const string DefaultStockAnalysisPrompt =
        "你是一名资深的A股分析师。请结合财务数据、技术指标、消息面、行业地位，对指定股票进行结构化分析，并给出风险提示与操作建议。";

    public AIService(
        StockDbContext context, 
        IHttpClientFactory httpClientFactory, 
        ILogger<AIService> logger, 
        IStockDataService stockDataService, 
        INewsService newsService,
        IIndustryService industryService,
        IMarketService marketService,
        IAIToolFactory toolFactory)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _stockDataService = stockDataService;
        _newsService = newsService;
        _industryService = industryService;
        _marketService = marketService;
        _toolFactory = toolFactory;
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

        var promptSettings = await GetPromptSettingsAsync(promptId);
        
        // 构造用户提示词，不再预先注入大量上下文，而是让AI自己决定调用工具
        var userInstruction = $"请分析股票 {stockCode}。";
        if (!string.IsNullOrWhiteSpace(additionalContext))
        {
            userInstruction += $" {additionalContext}";
        }

        var messages = new List<AiChatMessage>
        {
            new("user", userInstruction)
        };

        // 使用带工具的对话模式
        return await ChatWithToolsAsync(config, messages, promptSettings);
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

        var response = await ChatWithToolsAsync(config, conversation, promptSettings);
        return response;
    }

    /// <summary>
    /// 支持Function Calling的核心对话循环
    /// </summary>
    private async Task<string> ChatWithToolsAsync(AIModelConfig config, List<AiChatMessage> messages, AIPromptSettings settings)
    {
        const int MaxTurns = 10;
        int turns = 0;
        var tools = _toolFactory.GetAllDefinitions();
        
        // 初始化系统提示词
        var conversation = new List<object>(); // 使用object以支持不同结构的Message
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
        {
            conversation.Add(new { role = "system", content = settings.SystemPrompt });
        }
        
        conversation.AddRange(messages.Select(m => new 
        { 
            role = m.Role, 
            content = m.Content,
            tool_calls = m.ToolCalls,
            tool_call_id = m.ToolCallId
        }));

        while (turns++ < MaxTurns)
        {
            var response = await CallLlmApiAsync(config, conversation, settings.Temperature, tools);
            
            if (response == null) return "AI调用失败: 无响应";
            
            // 将AI的回复加入历史
            var assistantMsg = new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["content"] = response.Content
            };
            
            if (response.ToolCalls != null && response.ToolCalls.Any())
            {
                assistantMsg["tool_calls"] = response.ToolCalls;
                conversation.Add(assistantMsg);
                
                // 执行工具
                foreach (var toolCall in response.ToolCalls)
                {
                    _logger.LogInformation("Creating tool execution for {ToolName}", toolCall.Function.Name);
                    string toolResult = await _toolFactory.ExecuteToolAsync(toolCall.Function.Name, toolCall.Function.Arguments);
                    
                    conversation.Add(new 
                    {
                        role = "tool",
                        tool_call_id = toolCall.Id,
                        content = toolResult
                    });
                }
            }
            else
            {
                // 没有工具调用，直接返回内容
                return response.Content ?? "无内容";
            }
        }
        
        return "对话轮数超过限制";
    }
    


    // 兼容旧方法的重载
    private async Task<string> CallAIAsync(AIModelConfig config, IEnumerable<AiChatMessage> messages, AIPromptSettings settings)
    {
        // 旧方法不使用工具，直接适配到新API
        var conversation = new List<object>();
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
        {
            conversation.Add(new { role = "system", content = settings.SystemPrompt });
        }
        conversation.AddRange(messages.Select(m => new { role = m.Role, content = m.Content }));
        
        var resp = await CallLlmApiAsync(config, conversation, settings.Temperature, null);
        return resp?.Content ?? "调用失败";
    }

    private async Task<LlmResponse?> CallLlmApiAsync(AIModelConfig config, List<object> messages, double? temperature, List<AiTool>? tools)
    {
        try 
        {
            var requestBody = new
            {
                model = config.ModelName ?? "gpt-3.5-turbo",
                messages = messages,
                temperature = temperature,
                tools = (tools != null && tools.Any()) ? tools : null,
                stream = false
            };

            var endpoint = config.SubscribeEndpoint;
            if (string.IsNullOrEmpty(endpoint))
            {
                endpoint = config.Name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase) 
                    ? "https://api.deepseek.com/v1/chat/completions" 
                    : "https://api.openai.com/v1/chat/completions";
            }

            var content = new StringContent(JsonConvert.SerializeObject(requestBody, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), 
                System.Text.Encoding.UTF8, "application/json");
                
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

            using var httpClient = GetHttpClient();
            var response = await httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                 _logger.LogError("AI API Error: {StatusCode} {Json}", response.StatusCode, json);
                 return new LlmResponse { Content = $"API Error: {response.StatusCode}" };
            }

            dynamic? result = JsonConvert.DeserializeObject(json);
            
            // 解析标准OpenAI格式响应
            if (result?.choices != null && result.choices.Count > 0)
            {
                var choice = result.choices[0];
                var message = choice.message;
                
                var llmResp = new LlmResponse
                {
                    Content = message?.content,
                    FinishReason = choice?.finish_reason
                };

                if (message?.tool_calls != null)
                {
                    llmResp.ToolCalls = message.tool_calls.ToObject<List<AiToolCall>>();
                }
                
                return llmResp;
            }
            
            return new LlmResponse { Content = "无法解析响应" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CallLlmApiAsync Failed");
            return null;
        }
    }

    public async Task<string> GetStockRecommendationAsync(string stockCode)
    {
        var analysis = await AnalyzeStockAsync(stockCode, null, "请给出买入、持有或卖出的建议，并说明理由。");
        return analysis;
    }

    /// <summary>
    /// 获取股票的实时数据上下文，用于AI聊天
    /// </summary>
    public async Task<string> GetStockRealTimeDataContextAsync(string stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            return string.Empty;
        }

        stockCode = stockCode.Trim().ToUpperInvariant();
        var contextBuilder = new StringBuilder();

        try
        {
            // 1. 获取实时股价数据
            var stockQuote = await _stockDataService.GetWatchlistRealTimeQuoteAsync(stockCode);
            if (stockQuote != null)
            {
                contextBuilder.AppendLine($"📈 实时股价信息 ({DateTime.Now:yyyy-MM-dd HH:mm:ss}):");
                contextBuilder.AppendLine($"   股票代码: {stockQuote.Code}");
                contextBuilder.AppendLine($"   股票名称: {stockQuote.Name ?? "未知"}");
                contextBuilder.AppendLine($"   当前价格: ¥{stockQuote.CurrentPrice:F2}");
                contextBuilder.AppendLine($"   涨跌幅: {(stockQuote.ChangePercent >= 0 ? "+" : "")}{stockQuote.ChangePercent:F2}%");
                contextBuilder.AppendLine($"   涨跌额: ¥{stockQuote.ChangeAmount:F2}");
                if (stockQuote.HighPrice > 0)
                    contextBuilder.AppendLine($"   今日最高: ¥{stockQuote.HighPrice:F2}");
                if (stockQuote.LowPrice > 0)
                    contextBuilder.AppendLine($"   今日最低: ¥{stockQuote.LowPrice:F2}");
                if (stockQuote.Volume > 0)
                    contextBuilder.AppendLine($"   成交量: {stockQuote.Volume:N0} 手");
                if (stockQuote.Turnover > 0)
                    contextBuilder.AppendLine($"   成交额: ¥{stockQuote.Turnover:N0}");
                contextBuilder.AppendLine();
            }

            // 2. 获取最新新闻数据（最近3条）
            var newsList = await _newsService.GetNewsByStockAsync(stockCode);
            if (newsList != null && newsList.Any())
            {
                contextBuilder.AppendLine("📰 最新相关新闻 (最近3条):");
                var recentNews = newsList.OrderByDescending(n => n.PublishTime).Take(3);
                foreach (var news in recentNews)
                {
                    contextBuilder.AppendLine($"   • [{news.PublishTime:MM-dd HH:mm}] {news.Title}");
                    if (!string.IsNullOrWhiteSpace(news.Summary))
                    {
                        var summary = news.Summary.Length > 100 ? news.Summary[..100] + "..." : news.Summary;
                        contextBuilder.AppendLine($"     摘要: {summary}");
                    }
                }
                contextBuilder.AppendLine();
            }

            // 3. 获取基本面数据（如果有的话）
            var fundamentalData = await GetFundamentalDataAsync(stockCode);
            if (!string.IsNullOrWhiteSpace(fundamentalData))
            {
                contextBuilder.AppendLine("📊 基本面数据:");
                contextBuilder.AppendLine(fundamentalData);
                contextBuilder.AppendLine();
            }

            // 4. 获取技术指标（如果有的话）
            var technicalData = await GetTechnicalDataAsync(stockCode);
            if (!string.IsNullOrWhiteSpace(technicalData))
            {
                contextBuilder.AppendLine("📉 技术指标:");
                contextBuilder.AppendLine(technicalData);
                contextBuilder.AppendLine();
            }

            var result = contextBuilder.ToString().Trim();
            _logger.LogInformation("成功获取股票 {StockCode} 的实时数据上下文，长度: {Length}", stockCode, result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取股票 {StockCode} 实时数据上下文失败", stockCode);
            return string.Empty;
        }
    }

    /// <summary>
    /// 获取基本面数据
    /// </summary>
    private async Task<string> GetFundamentalDataAsync(string stockCode)
    {
        try
        {
            // 这里可以扩展获取财务数据、行业数据等
            // 目前先返回空字符串，未来可以从数据库或API获取
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取股票 {StockCode} 基本面数据失败", stockCode);
            return string.Empty;
        }
    }

    /// <summary>
    /// 获取技术指标数据
    /// </summary>
    private async Task<string> GetTechnicalDataAsync(string stockCode)
    {
        try
        {
            // 这里可以扩展获取技术指标如MACD、RSI、KDJ等
            // 目前先返回空字符串，未来可以从数据库或计算获取
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取股票 {StockCode} 技术指标数据失败", stockCode);
            return string.Empty;
        }
    }

    private static string ApplyPlaceholders(string template, IDictionary<string, string?>? placeholders)
    {
        if (placeholders == null || !placeholders.Any())
        {
            return template;
        }

        var result = template;
        foreach (var kv in placeholders)
        {
            if (!string.IsNullOrEmpty(kv.Key))
            {
                result = result.Replace(kv.Key, kv.Value ?? string.Empty);
            }
        }
        return result;
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
        // 3) 硬编码默认值回退（数据库中没有默认提示词时的最后保障）
        return new AIPromptSettings 
        { 
            SystemPrompt = DefaultStockAnalysisPrompt, 
            Temperature = 0.7 
        };
    }
}

