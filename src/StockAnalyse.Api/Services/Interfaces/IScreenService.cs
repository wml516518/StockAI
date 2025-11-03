using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IScreenService
{
    /// <summary>
    /// 条件选股（返回分页结果）
    /// </summary>
    Task<PagedResult<Stock>> ScreenStocksAsync(ScreenCriteria criteria);
    
    /// <summary>
    /// 条件选股（返回全部结果，兼容旧接口）
    /// </summary>
    Task<List<Stock>> ScreenStocksAllAsync(ScreenCriteria criteria);
}


