using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface INewsService
{
    /// <summary>
    /// 获取最新金融消息
    /// </summary>
    Task<List<FinancialNews>> GetLatestNewsAsync(int count = 50);
    
    /// <summary>
    /// 获取指定股票的新闻
    /// </summary>
    Task<List<FinancialNews>> GetNewsByStockAsync(string stockCode);
    
    /// <summary>
    /// 从数据源抓取新闻
    /// </summary>
    Task FetchNewsAsync();
    
    /// <summary>
    /// 搜索新闻
    /// </summary>
    Task<List<FinancialNews>> SearchNewsAsync(string keyword);
}


