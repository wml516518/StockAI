using System.Collections.Generic;
using StockAnalyse.Api.Services.Abstractions;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IAIService
{
    /// <summary>
    /// 分析股票（可指定提示词）
    /// </summary>
    Task<string> AnalyzeStockAsync(string stockCode, int? promptId = null, string? additionalContext = null, int? modelId = null);
    
    /// <summary>
    /// 使用指定提示词名称执行AI分析
    /// </summary>
    Task<string> ExecutePromptAsync(string? promptName, string userPrompt, IDictionary<string, string?>? placeholders = null, int? modelId = null);
    
    /// <summary>
    /// 问询大模型
    /// </summary>
    Task<string> ChatAsync(IEnumerable<AiChatMessage> messages, string? context = null, int? modelId = null, int maxHistory = 5);
    
    /// <summary>
    /// 获取股票建议
    /// </summary>
    Task<string> GetStockRecommendationAsync(string stockCode);
}


