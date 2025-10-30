using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IScreenService
{
    /// <summary>
    /// 条件选股
    /// </summary>
    Task<List<Stock>> ScreenStocksAsync(ScreenCriteria criteria);
}


