using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IStockDataService
{
    /// <summary>
    /// 获取股票实时行情
    /// </summary>
    Task<Stock?> GetRealTimeQuoteAsync(string stockCode);
    
    /// <summary>
    /// 获取自选股实时行情（总是从接口获取，不使用缓存数据）
    /// </summary>
    Task<Stock?> GetWatchlistRealTimeQuoteAsync(string stockCode);
    
    /// <summary>
    /// 批量获取股票行情
    /// </summary>
    Task<List<Stock>> GetBatchQuotesAsync(IEnumerable<string> stockCodes);
    
    /// <summary>
    /// 保存股票数据
    /// </summary>
    Task SaveStockDataAsync(Stock stock);
    
    /// <summary>
    /// 获取日线数据
    /// </summary>
    Task<List<StockHistory>> GetDailyDataAsync(string stockCode, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// 计算MACD指标（注意：不再更新数据库中的MACD字段）
    /// </summary>
    Task<(decimal macd, decimal signal, decimal histogram)> CalculateMACDAsync(string stockCode);
    
    /// <summary>
    /// 获取股票排名列表
    /// </summary>
    Task<List<Stock>> GetRankingListAsync(string market, int top = 100);

    /// <summary>
    /// 从东方财富拉取日线历史并保存到数据库（存在则更新）
    /// </summary>
    Task<int> FetchAndStoreDailyHistoryAsync(string stockCode, DateTime startDate, DateTime endDate);
}


