namespace StockAnalyse.Api.Services.Interfaces;

public interface ITradingPlanService
{
    /// <summary>
    /// 生成做T方案
    /// </summary>
    Task<TradingPlanResult> GenerateTradingPlanAsync(string stockCode);

    /// <summary>
    /// 更新指定自选股的做T方案
    /// </summary>
    Task UpdateTradingPlanForStockAsync(int watchlistStockId, bool force = false);


}

public class TradingPlanResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string BuyPriceRange { get; set; } = string.Empty;
    public string SellPriceRange { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public DateTime UpdateTime { get; set; }
}

