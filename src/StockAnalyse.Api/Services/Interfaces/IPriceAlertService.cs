using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IPriceAlertService
{
    /// <summary>
    /// 检查自选股建议价格提醒
    /// </summary>
    Task CheckSuggestedPriceAlertsAsync();
}

