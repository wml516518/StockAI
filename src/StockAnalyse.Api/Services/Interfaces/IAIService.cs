namespace StockAnalyse.Api.Services.Interfaces;

public interface IAIService
{
    /// <summary>
    /// 分析股票（可指定提示词）
    /// </summary>
    Task<string> AnalyzeStockAsync(string stockCode, int? promptId = null, string? additionalContext = null);
    
    /// <summary>
    /// 问询大模型
    /// </summary>
    Task<string> ChatAsync(string message, string? context = null);
    
    /// <summary>
    /// 获取股票建议
    /// </summary>
    Task<string> GetStockRecommendationAsync(string stockCode);
}


