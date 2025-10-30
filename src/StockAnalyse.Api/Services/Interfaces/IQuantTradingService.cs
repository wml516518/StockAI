using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

/// <summary>
/// 量化交易服务接口
/// </summary>
public interface IQuantTradingService
{
    /// <summary>
    /// 获取所有策略
    /// </summary>
    Task<List<QuantStrategy>> GetAllStrategiesAsync();
    
    /// <summary>
    /// 根据ID获取策略
    /// </summary>
    Task<QuantStrategy?> GetStrategyByIdAsync(int id);
    
    /// <summary>
    /// 创建策略
    /// </summary>
    Task<QuantStrategy> CreateStrategyAsync(QuantStrategy strategy);
    
    /// <summary>
    /// 更新策略
    /// </summary>
    Task<QuantStrategy> UpdateStrategyAsync(QuantStrategy strategy);
    
    /// <summary>
    /// 删除策略
    /// </summary>
    Task<bool> DeleteStrategyAsync(int id);
    
    /// <summary>
    /// 运行策略（生成交易信号）
    /// </summary>
    Task<List<TradingSignal>> RunStrategyAsync(int strategyId, List<string>? stockCodes = null);
    
    /// <summary>
    /// 执行交易信号
    /// </summary>
    Task<SimulatedTrade?> ExecuteSignalAsync(int signalId);
    
    /// <summary>
    /// 获取策略的交易信号
    /// </summary>
    Task<List<TradingSignal>> GetStrategySignalsAsync(int strategyId, DateTime? startDate = null, DateTime? endDate = null);
    
    /// <summary>
    /// 获取策略的交易记录
    /// </summary>
    Task<List<SimulatedTrade>> GetStrategyTradesAsync(int strategyId, DateTime? startDate = null, DateTime? endDate = null);
}