using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

/// <summary>
/// 回测服务接口
/// </summary>
public interface IBacktestService
{
    /// <summary>
    /// 运行回测（使用自选股集合）
    /// </summary>
    Task<BacktestResult> RunBacktestAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital = 100000);
    
    /// <summary>
    /// 获取策略的回测结果（历史记录）
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
    
    // 新增：批量股票回测（指定股票集合）
    // 新增：针对指定股票集合的批量回测，返回每只股票的独立结果列表
    Task<BacktestResult> RunBacktestAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital, System.Collections.Generic.List<string> stockCodes);

    /// <summary>
    /// 批量股票回测（每只股票独立资金账户，返回结果列表，不入库）
    /// </summary>
    Task<System.Collections.Generic.List<StockBacktestSummary>> RunBatchBacktestAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital, System.Collections.Generic.List<string> stockCodes);
}