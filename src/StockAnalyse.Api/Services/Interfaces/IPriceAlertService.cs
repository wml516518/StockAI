using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IPriceAlertService
{
    /// <summary>
    /// 创建价格提醒
    /// </summary>
    Task<PriceAlert> CreateAlertAsync(string stockCode, decimal targetPrice, AlertType type);
    
    /// <summary>
    /// 获取所有活跃的提醒
    /// </summary>
    Task<List<PriceAlert>> GetActiveAlertsAsync();
    
    /// <summary>
    /// 检查并触发提醒
    /// </summary>
    Task CheckAndTriggerAlertsAsync();
    
    /// <summary>
    /// 删除提醒
    /// </summary>
    Task<bool> DeleteAlertAsync(int id);
    
    /// <summary>
    /// 检查自选股建议价格提醒
    /// </summary>
    Task CheckSuggestedPriceAlertsAsync();
}

