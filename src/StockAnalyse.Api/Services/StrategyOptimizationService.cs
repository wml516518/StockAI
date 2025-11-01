using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Diagnostics;
using System.Text.Json;

namespace StockAnalyse.Api.Services;

public class StrategyOptimizationService : IStrategyOptimizationService
{
    private readonly StockDbContext _context;
    private readonly IBacktestService _backtestService;
    private readonly ILogger<StrategyOptimizationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public StrategyOptimizationService(
        StockDbContext context,
        IBacktestService backtestService,
        ILogger<StrategyOptimizationService> logger)
    {
        _context = context;
        _backtestService = backtestService;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task<StrategyOptimizationResult> OptimizeStrategyAsync(
        int strategyId, 
        List<string> stockCodes, 
        DateTime startDate, 
        DateTime endDate,
        OptimizationConfig optimizationConfig)
    {
        var strategy = await _context.QuantStrategies.FindAsync(strategyId);
        if (strategy == null)
            throw new ArgumentException($"策略 {strategyId} 不存在");

        var stopwatch = Stopwatch.StartNew();
        
        // 创建优化结果记录
        var optimizationResult = new StrategyOptimizationResult
        {
            StrategyId = strategyId,
            StartDate = startDate,
            EndDate = endDate,
            StockCodes = JsonSerializer.Serialize(stockCodes, _jsonOptions),
            OptimizationConfig = JsonSerializer.Serialize(optimizationConfig, _jsonOptions)
        };

        _context.StrategyOptimizationResults.Add(optimizationResult);
        await _context.SaveChangesAsync();

        try
        {
            // 生成参数组合
            var parameterCombinations = GenerateParameterCombinations(optimizationConfig);
            optimizationResult.TotalCombinations = parameterCombinations.Count;
            optimizationResult.TestedCombinations = 0;

            _logger.LogInformation("开始优化策略 {StrategyId}，共 {Count} 个参数组合", strategyId, parameterCombinations.Count);

            var bestResult = new ParameterTestResult();
            var bestScore = decimal.MinValue;

            // 使用信号量控制并发数
            using var semaphore = new SemaphoreSlim(optimizationConfig.MaxConcurrency);
            var tasks = parameterCombinations.Select(async parameters =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await TestParameterCombinationAsync(strategyId, stockCodes, startDate, endDate, parameters, optimizationResult.Id);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);

            // 找到最佳结果
            foreach (var result in results.Where(r => r != null))
            {
                var score = CalculateScore(result!, optimizationConfig.Target);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult = result!;
                }

                optimizationResult.TestedCombinations++;
            }

            // 更新优化结果
            stopwatch.Stop();
            optimizationResult.OptimizationDuration = stopwatch.Elapsed;
            optimizationResult.OptimizedParameters = bestResult.Parameters;
            optimizationResult.TotalReturn = bestResult.TotalReturn;
            optimizationResult.SharpeRatio = bestResult.SharpeRatio;
            optimizationResult.MaxDrawdown = bestResult.MaxDrawdown;
            optimizationResult.WinRate = bestResult.WinRate;
            optimizationResult.TotalTrades = bestResult.TotalTrades;

            await _context.SaveChangesAsync();

            _logger.LogInformation("策略 {StrategyId} 优化完成，最佳收益率: {TotalReturn:P2}，夏普比率: {SharpeRatio:F2}", 
                strategyId, optimizationResult.TotalReturn, optimizationResult.SharpeRatio);

            return optimizationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "优化策略 {StrategyId} 时发生错误", strategyId);
            throw;
        }
    }

    public async Task<List<StrategyOptimizationResult>> GetOptimizationHistoryAsync(int strategyId)
    {
        return await _context.StrategyOptimizationResults
            .Where(r => r.StrategyId == strategyId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> ApplyOptimalParametersAsync(int strategyId, int optimizationResultId)
    {
        var strategy = await _context.QuantStrategies.FindAsync(strategyId);
        var optimizationResult = await _context.StrategyOptimizationResults.FindAsync(optimizationResultId);

        if (strategy == null || optimizationResult == null || optimizationResult.StrategyId != strategyId)
            return false;

        // 应用优化后的参数
        strategy.Parameters = optimizationResult.OptimizedParameters;
        strategy.UpdatedAt = DateTime.Now;

        // 标记优化结果已应用
        optimizationResult.IsApplied = true;

        await _context.SaveChangesAsync();

        _logger.LogInformation("已将优化结果 {OptimizationResultId} 应用到策略 {StrategyId}", optimizationResultId, strategyId);
        return true;
    }

    public async Task<List<StrategyOptimizationResult>> BatchOptimizeStrategiesAsync(
        List<int> strategyIds,
        List<string> stockCodes,
        DateTime startDate,
        DateTime endDate,
        OptimizationConfig optimizationConfig)
    {
        var results = new List<StrategyOptimizationResult>();

        foreach (var strategyId in strategyIds)
        {
            try
            {
                var result = await OptimizeStrategyAsync(strategyId, stockCodes, startDate, endDate, optimizationConfig);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量优化策略 {StrategyId} 时发生错误", strategyId);
            }
        }

        return results;
    }

    private List<TechnicalIndicatorParameters> GenerateParameterCombinations(OptimizationConfig config)
    {
        var combinations = new List<TechnicalIndicatorParameters>();

        for (int shortPeriod = config.ShortPeriodRange.Min; 
             shortPeriod <= config.ShortPeriodRange.Max; 
             shortPeriod += config.ShortPeriodRange.Step)
        {
            for (int longPeriod = config.LongPeriodRange.Min; 
                 longPeriod <= config.LongPeriodRange.Max; 
                 longPeriod += config.LongPeriodRange.Step)
            {
                // 确保短周期小于长周期
                if (shortPeriod >= longPeriod) continue;

                for (int rsiOverBought = config.RSIOverBoughtRange.Min;
                     rsiOverBought <= config.RSIOverBoughtRange.Max;
                     rsiOverBought += config.RSIOverBoughtRange.Step)
                {
                    for (int rsiOverSold = config.RSIOverSoldRange.Min;
                         rsiOverSold <= config.RSIOverSoldRange.Max;
                         rsiOverSold += config.RSIOverSoldRange.Step)
                    {
                        // 确保超卖线小于超买线
                        if (rsiOverSold >= rsiOverBought) continue;

                        combinations.Add(new TechnicalIndicatorParameters
                        {
                            ShortPeriod = shortPeriod,
                            LongPeriod = longPeriod,
                            RSIOverBought = rsiOverBought,
                            RSIOverSold = rsiOverSold,
                            // 使用默认的MACD参数
                            FastPeriod = 12,
                            SlowPeriod = 26,
                            SignalPeriod = 9,
                            RSIPeriod = 14
                        });
                    }
                }
            }
        }

        return combinations;
    }

    private async Task<ParameterTestResult?> TestParameterCombinationAsync(
        int strategyId,
        List<string> stockCodes,
        DateTime startDate,
        DateTime endDate,
        TechnicalIndicatorParameters parameters,
        int optimizationResultId)
    {
        try
        {
            // 创建临时策略用于测试
            var strategy = await _context.QuantStrategies.FindAsync(strategyId);
            if (strategy == null) return null;

            var tempStrategy = new QuantStrategy
            {
                Name = $"Temp_{strategy.Name}_{Guid.NewGuid():N}",
                Description = "临时优化测试策略",
                Type = strategy.Type,
                Parameters = JsonSerializer.Serialize(parameters, _jsonOptions),
                InitialCapital = strategy.InitialCapital,
                CurrentCapital = strategy.InitialCapital,
                IsActive = false
            };

            _context.QuantStrategies.Add(tempStrategy);
            await _context.SaveChangesAsync();

            try
            {
                // 运行回测
                var backtestResult = await _backtestService.RunBacktestAsync(tempStrategy.Id, startDate, endDate, strategy.InitialCapital, stockCodes);

                if (backtestResult == null) return null;

                // 创建参数测试结果
                var testResult = new ParameterTestResult
                {
                    OptimizationResultId = optimizationResultId,
                    Parameters = JsonSerializer.Serialize(parameters, _jsonOptions),
                    TotalReturn = backtestResult.TotalReturn,
                    SharpeRatio = backtestResult.SharpeRatio,
                    MaxDrawdown = backtestResult.MaxDrawdown,
                    WinRate = backtestResult.WinRate,
                    TotalTrades = backtestResult.TotalTrades
                };

                _context.ParameterTestResults.Add(testResult);
                await _context.SaveChangesAsync();

                return testResult;
            }
            finally
            {
                // 清理临时策略
                _context.QuantStrategies.Remove(tempStrategy);
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "测试参数组合时发生错误: {Parameters}", JsonSerializer.Serialize(parameters, _jsonOptions));
            return null;
        }
    }

    private decimal CalculateScore(ParameterTestResult result, OptimizationTarget target)
    {
        return target switch
        {
            OptimizationTarget.TotalReturn => result.TotalReturn,
            OptimizationTarget.SharpeRatio => result.SharpeRatio,
            OptimizationTarget.MaxDrawdown => -result.MaxDrawdown, // 最大回撤越小越好
            OptimizationTarget.WinRate => result.WinRate,
            _ => result.TotalReturn
        };
    }
}