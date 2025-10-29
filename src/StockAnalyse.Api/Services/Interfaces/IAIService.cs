namespace StockAnalyse.Api.Services.Interfaces;

public interface IAIService
{
    /// <summary>
    /// 分析股票
    /// </summary>
    Task<string> AnalyzeStockAsync(string stockCode, string? additionalContext = null);
    
    /// <summary>
    /// 问询大模型
    /// </summary>
    Task<string> ChatAsync(string message, string? context = null);
    
    /// <summary>
    /// 获取股票建议
    /// </summary>
    Task<string> GetStockRecommendationAsync(string stockCode);
}


