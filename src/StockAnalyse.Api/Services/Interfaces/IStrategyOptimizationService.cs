using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

/// <summary>
/// 策略优化服务接口
/// </summary>
public interface IStrategyOptimizationService
{
    /// <summary>
    /// 优化策略参数（网格搜索）
    /// </summary>
    /// <param name="strategyId">策略ID</param>
    /// <param name="stockCodes">股票代码列表</param>
    /// <param name="startDate">回测开始日期</param>
    /// <param name="endDate">回测结束日期</param>
    /// <param name="optimizationConfig">优化配置</param>
    /// <returns>优化结果</returns>
    Task<StrategyOptimizationResult> OptimizeStrategyAsync(
        int strategyId, 
        List<string> stockCodes, 
        DateTime startDate, 
        DateTime endDate,
        OptimizationConfig optimizationConfig);

    /// <summary>
    /// 获取优化历史记录
    /// </summary>
    /// <param name="strategyId">策略ID</param>
    /// <returns>优化历史记录</returns>
    Task<List<StrategyOptimizationResult>> GetOptimizationHistoryAsync(int strategyId);

    /// <summary>
    /// 应用最优参数到策略
    /// </summary>
    /// <param name="strategyId">策略ID</param>
    /// <param name="optimizationResultId">优化结果ID</param>
    /// <returns>是否成功</returns>
    Task<bool> ApplyOptimalParametersAsync(int strategyId, int optimizationResultId);

    /// <summary>
    /// 批量优化多个策略
    /// </summary>
    /// <param name="strategyIds">策略ID列表</param>
    /// <param name="stockCodes">股票代码列表</param>
    /// <param name="startDate">回测开始日期</param>
    /// <param name="endDate">回测结束日期</param>
    /// <param name="optimizationConfig">优化配置</param>
    /// <returns>批量优化结果</returns>
    Task<List<StrategyOptimizationResult>> BatchOptimizeStrategiesAsync(
        List<int> strategyIds,
        List<string> stockCodes,
        DateTime startDate,
        DateTime endDate,
        OptimizationConfig optimizationConfig);
}

/// <summary>
/// 优化配置
/// </summary>
public class OptimizationConfig
{
    /// <summary>
    /// 优化目标（总收益率、夏普比率、最大回撤等）
    /// </summary>
    public OptimizationTarget Target { get; set; } = OptimizationTarget.TotalReturn;

    /// <summary>
    /// 短周期参数范围
    /// </summary>
    public ParameterRange ShortPeriodRange { get; set; } = new() { Min = 5, Max = 20, Step = 1 };

    /// <summary>
    /// 长周期参数范围
    /// </summary>
    public ParameterRange LongPeriodRange { get; set; } = new() { Min = 20, Max = 60, Step = 5 };

    /// <summary>
    /// RSI超买阈值范围
    /// </summary>
    public ParameterRange RSIOverBoughtRange { get; set; } = new() { Min = 70, Max = 85, Step = 5 };

    /// <summary>
    /// RSI超卖阈值范围
    /// </summary>
    public ParameterRange RSIOverSoldRange { get; set; } = new() { Min = 15, Max = 30, Step = 5 };

    /// <summary>
    /// 最大并发数
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;
}

/// <summary>
/// 参数范围
/// </summary>
public class ParameterRange
{
    public int Min { get; set; }
    public int Max { get; set; }
    public int Step { get; set; } = 1;
}

/// <summary>
/// 优化目标枚举
/// </summary>
public enum OptimizationTarget
{
    TotalReturn,    // 总收益率
    SharpeRatio,    // 夏普比率
    MaxDrawdown,    // 最大回撤（最小化）
    WinRate         // 胜率
}