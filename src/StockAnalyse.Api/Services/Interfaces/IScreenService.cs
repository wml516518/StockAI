using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IScreenService
{
    /// <summary>
    /// 条件选股
    /// </summary>
    Task<List<Stock>> ScreenStocksAsync(ScreenCriteria criteria);
}

/// <summary>
/// 选股条件
/// </summary>
public class ScreenCriteria
{
    public string? Market { get; set; } // 市场：SH/SZ
    
    // 价格条件
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    
    // 涨跌幅条件
    public decimal? MinChangePercent { get; set; }
    public decimal? MaxChangePercent { get; set; }
    
    // 换手率条件
    public decimal? MinTurnoverRate { get; set; }
    public decimal? MaxTurnoverRate { get; set; }
    
    // 基本面条件
    public decimal? MinPE { get; set; }
    public decimal? MaxPE { get; set; }
    public decimal? MinPB { get; set; }
    public decimal? MaxPB { get; set; }
}


