using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface INewsService
{
    /// <summary>
    /// 获取指定股票的新闻
    /// </summary>
    Task<List<FinancialNews>> GetNewsByStockAsync(string stockCode, bool forceRefresh = false, CancellationToken cancellationToken = default);
}


