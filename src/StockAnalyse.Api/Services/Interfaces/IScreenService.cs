using Newtonsoft.Json.Linq;
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

    /// <summary>
    /// 获取热点题材成交量放大短线策略结果（来自Python服务，返回JSON字符串以避免重复序列化问题）
    /// </summary>
    Task<string> GetShortTermHotStrategyAsync(int topHot, int topThemes, int themeMembers);
}


