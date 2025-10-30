using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

/// <summary>
/// 回测服务接口
/// </summary>
public interface IBacktestService
{
    /// <summary>
    /// 运行回测
    /// </summary>
    Task<BacktestResult> RunBacktestAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital = 100000);
    
    /// <summary>
    /// 获取策略的回测结果
    /// </summary>
    Task<List<BacktestResult>> GetBacktestResultsAsync(int strategyId);
    
    /// <summary>
    /// 获取回测详细结果
    /// </summary>
    Task<BacktestResult?> GetBacktestResultByIdAsync(int id);
    
    /// <summary>
    /// 删除回测结果
    /// </summary>
    Task<bool> DeleteBacktestResultAsync(int id);
    
    /// <summary>
    /// 计算策略性能指标
    /// </summary>
    Task<(decimal totalReturn, decimal annualizedReturn, decimal sharpeRatio, decimal maxDrawdown, decimal winRate)> CalculatePerformanceMetricsAsync(List<SimulatedTrade> trades, decimal initialCapital, DateTime startDate, DateTime endDate);
}