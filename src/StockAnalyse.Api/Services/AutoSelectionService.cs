using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace StockAnalyse.Api.Services;

/// <summary>
/// 自动选股服务实现
/// </summary>
public class AutoSelectionService : IAutoSelectionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoSelectionService> _logger;
    
    // 防止重复执行的锁
    private readonly SemaphoreSlim _executionLock = new SemaphoreSlim(1, 1);
    private bool _isExecuting = false;

    // 配置：选股规则 - 技术面条件
    private const decimal MinPrice = 8.0m; // 最低价格（过滤低价垃圾股）
    private const decimal MaxPrice = 50.0m; // 最高价格（避免高价股风险）
    private const decimal MinChangePercent = 0.0m; // 最低涨跌幅（0%以上，避免下跌股）
    private const decimal MaxChangePercent = 7.0m; // 最高涨跌幅（7%以下，避免追高）
    private const decimal MinTurnoverRate = 2.0m; // 最低换手率（2%以上，有一定活跃度）
    private const decimal MaxTurnoverRate = 12.0m; // 最高换手率（12%以下，避免庄家出货）
    private const decimal MinVolume = 2000000m; // 最低成交量（200万手，确保流动性）

    // 配置：AI评分阈值
    private const int MinAIScore = 7; // AI评分最低分（0-10分制）

    // 配置：每次选股数量限制
    private const int MaxSelectionCount = 20; // 每次最多选20只股票

    public AutoSelectionService(
        IServiceProvider serviceProvider,
        ILogger<AutoSelectionService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// 执行自动选股（不保存到自选股，只返回结果）
    /// </summary>
    public async Task<AutoSelectionResult> ExecuteSelectionAsync(CancellationToken cancellationToken = default)
    {
        var executionId = Guid.NewGuid().ToString("N")[..8]; // 生成执行ID用于追踪
        var startTime = DateTime.Now;
        
        // 防止重复执行：如果正在执行，直接返回错误
        if (!await _executionLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("[执行ID: {ExecutionId}] 自动选股任务正在执行中，拒绝重复请求 - 当前时间: {Time:yyyy-MM-dd HH:mm:ss.fff}", 
                executionId, DateTime.Now);
            return new AutoSelectionResult
            {
                Success = false,
                ErrorMessage = "自动选股任务正在执行中，请勿重复请求"
            };
        }

        try
        {
            _isExecuting = true;
            _logger.LogInformation("[执行ID: {ExecutionId}] 开始执行自动选股流程 - 时间: {Time:yyyy-MM-dd HH:mm:ss.fff}", 
                executionId, startTime);
            
            using var scope = _serviceProvider.CreateScope();
            var stockDataService = scope.ServiceProvider.GetRequiredService<IStockDataService>();
            var industryService = scope.ServiceProvider.GetRequiredService<IIndustryService>();
            var marketService = scope.ServiceProvider.GetRequiredService<IMarketService>();
            var aiService = scope.ServiceProvider.GetRequiredService<IAIService>();

            try
            {

            // 步骤1: 获取所有股票数据
            _logger.LogInformation("步骤1: 从腾讯财经获取所有股票数据...");
            var allStocks = await stockDataService.FetchAllStocksFromTencentAsync(null, 2000);
            _logger.LogInformation("获取到 {Count} 只股票", allStocks.Count);

            if (allStocks.Count == 0)
            {
                _logger.LogWarning("未获取到任何股票数据，跳过本次选股");
                return new AutoSelectionResult
                {
                    Success = true,
                    TotalStocks = 0,
                    FilteredCount = 0,
                    ScoredCount = 0,
                    SelectedCount = 0,
                    SelectedStocks = new List<AutoSelectedStock>()
                };
            }

            // 步骤2: 应用快速技术面和资金面筛选规则（第一层快速过滤）
            _logger.LogInformation("步骤2: 应用快速技术面和资金面筛选规则（第一层过滤）...");
            var quickFilteredStocks = ApplyQuickFilterRules(allStocks);
            _logger.LogInformation("快速筛选后剩余 {Count} 只股票", quickFilteredStocks.Count);

            if (quickFilteredStocks.Count == 0)
            {
                _logger.LogInformation("快速筛选后没有符合条件的股票，跳过本次选股");
                return new AutoSelectionResult
                {
                    Success = true,
                    TotalStocks = allStocks.Count,
                    FilteredCount = 0,
                    ScoredCount = 0,
                    SelectedCount = 0,
                    SelectedStocks = new List<AutoSelectedStock>()
                };
            }

            // 步骤2.1: 应用深度筛选（基本面、技术面深度检查、新闻过滤）
            _logger.LogInformation("步骤2.1: 应用深度筛选（基本面、技术面、新闻）...");
            var autoFilterService = scope.ServiceProvider.GetRequiredService<IAutoFilterService>();
            var (filteredStocks, filterStatistics) = await ApplyDeepFilterRulesAsync(quickFilteredStocks, autoFilterService, cancellationToken);
            _logger.LogInformation("深度筛选后剩余 {Count} 只股票", filteredStocks.Count);
            
            // 输出详细统计信息
            _logger.LogInformation("=== 深度筛选完成统计 ===");
            _logger.LogInformation("总数: {Total}, 最终通过: {All}", filterStatistics.TotalChecked, filterStatistics.PassedAll);
            _logger.LogInformation("基本面: 通过={Passed}, 失败={Failed}", filterStatistics.PassedFundamental, filterStatistics.FilteredByFundamental);
            _logger.LogInformation("  - 数据缺失: {DataMissing}", filterStatistics.FilteredByFundamentalDataMissing);
            _logger.LogInformation("  - 净利润<=0: {NetProfit}", filterStatistics.FilteredByNetProfit);
            _logger.LogInformation("  - ROE<=6%: {ROE}", filterStatistics.FilteredByROE);
            _logger.LogInformation("  - 毛利率<=15%: {GrossMargin}", filterStatistics.FilteredByGrossProfitMargin);
            _logger.LogInformation("  - 营收同比<=0: {RevenueGrowth}", filterStatistics.FilteredByRevenueGrowth);
            _logger.LogInformation("  - ST类股票: {ST}", filterStatistics.FilteredByST);
            _logger.LogInformation("  - 资产负债率>=70%: {AssetLiability}", filterStatistics.FilteredByAssetLiabilityRatio);
            _logger.LogInformation("  - 流动比率<=1: {CurrentRatio}", filterStatistics.FilteredByCurrentRatio);
            _logger.LogInformation("  - 行业不符合: {Industry} (共{Count}个不同行业)", 
                filterStatistics.FilteredByIndustry, filterStatistics.FilteredIndustries.Count);
            
            // 输出被过滤的行业详情（按数量排序，只显示前10个）
            if (filterStatistics.FilteredIndustries.Count > 0)
            {
                var topFilteredIndustries = filterStatistics.FilteredIndustries.OrderByDescending(kvp => kvp.Value).Take(10).ToList();
                _logger.LogInformation("  - 被过滤行业详情（Top 10）:");
                foreach (var kvp in topFilteredIndustries)
                {
                    _logger.LogInformation("    * {IndustryName}: {Count}只", kvp.Key, kvp.Value);
                }
            }
            
            _logger.LogInformation("技术面: 通过={Passed}, 失败={Failed}", filterStatistics.PassedTechnical, filterStatistics.FilteredByTechnical);
            _logger.LogInformation("  - 数据缺失: {DataMissing}", filterStatistics.FilteredByTechnicalDataMissing);
            _logger.LogInformation("  - 未满足反转信号: {ReversalSignal}", filterStatistics.FilteredByReversalSignal);
            _logger.LogInformation("  - 成交量不足: {Volume}", filterStatistics.FilteredByVolume);
            _logger.LogInformation("  - 换手率不在2%-12%: {TurnoverRate}", filterStatistics.FilteredByTurnoverRate);
            _logger.LogInformation("  - 涨幅>7%: {ChangePercent}", filterStatistics.FilteredByChangePercent);
            _logger.LogInformation("  - 波动率>=6%: {Volatility}", filterStatistics.FilteredByVolatility);
            _logger.LogInformation("新闻: 通过={Passed}, 失败={Failed} (负面新闻: {Negative})", 
                filterStatistics.PassedNews, filterStatistics.FilteredByNews, filterStatistics.FilteredByNewsNegative);

            if (filteredStocks.Count == 0)
            {
                _logger.LogInformation("筛选后没有符合条件的股票，跳过本次选股");
                return new AutoSelectionResult
                {
                    Success = true,
                    TotalStocks = allStocks.Count,
                    FilteredCount = 0,
                    ScoredCount = 0,
                    SelectedCount = 0,
                    SelectedStocks = new List<AutoSelectedStock>()
                };
            }

            // 步骤3: 丰富数据（行业、人气榜）
            _logger.LogInformation("步骤3: 丰富股票数据（行业、人气榜）...");
            var enrichedStocks = await EnrichStockDataAsync(filteredStocks, industryService, marketService, cancellationToken);
            _logger.LogInformation("数据丰富完成，共 {Count} 只股票", enrichedStocks.Count);

            // 步骤4: AI评分
            _logger.LogInformation("步骤4: 使用AI对股票进行评分...");
            var scoredStocks = await ScoreStocksWithAIAsync(enrichedStocks, aiService, cancellationToken);
            _logger.LogInformation("AI评分完成，共 {Count} 只股票", scoredStocks.Count);

            // 步骤5: 筛选高分股票
            _logger.LogInformation("步骤5: 筛选高分股票...");
            var highScoreStocks = scoredStocks
                .Where(s => s.AIScore >= MinAIScore)
                .OrderByDescending(s => s.AIScore)
                .Take(MaxSelectionCount)
                .ToList();

            if (highScoreStocks.Count == 0)
            {
                _logger.LogInformation("没有达到评分阈值的股票（最低分: {MinScore}）", MinAIScore);
                return new AutoSelectionResult
                {
                    Success = true,
                    TotalStocks = allStocks.Count,
                    FilteredCount = filteredStocks.Count,
                    ScoredCount = scoredStocks.Count,
                    SelectedCount = 0,
                    SelectedStocks = new List<AutoSelectedStock>()
                };
            }

            // 构建结果
            var result = new AutoSelectionResult
            {
                Success = true,
                TotalStocks = allStocks.Count,
                FilteredCount = filteredStocks.Count,
                ScoredCount = scoredStocks.Count,
                SelectedCount = highScoreStocks.Count,
                SelectedStocks = highScoreStocks.Select(s => new AutoSelectedStock
                {
                    StockCode = s.Stock.Code,
                    StockName = s.Stock.Name,
                    CurrentPrice = s.Stock.CurrentPrice,
                    ChangePercent = s.Stock.ChangePercent,
                    TurnoverRate = s.Stock.TurnoverRate,
                    Volume = s.Stock.Volume,
                    AIScore = s.AIScore,
                    IndustryName = s.IndustryInfo?.IndustryName,
                    HotRank = s.HotRank
                }).ToList()
            };

                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("[执行ID: {ExecutionId}] 自动选股任务完成 - 耗时: {Elapsed:F2}秒, 筛选出 {Count} 只股票", 
                    executionId, elapsed, highScoreStocks.Count);
                return result;
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogError(ex, "[执行ID: {ExecutionId}] 执行自动选股任务时发生错误 - 耗时: {Elapsed:F2}秒", 
                    executionId, elapsed);
                return new AutoSelectionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        finally
        {
            _isExecuting = false;
            _executionLock.Release();
            var totalElapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("[执行ID: {ExecutionId}] 自动选股任务执行完成，释放执行锁 - 总耗时: {Elapsed:F2}秒", 
                executionId, totalElapsed);
        }
    }

    /// <summary>
    /// 应用快速技术面和资金面筛选规则（第一层快速过滤，不涉及异步操作）
    /// </summary>
    private List<Stock> ApplyQuickFilterRules(List<Stock> stocks)
    {
        return stocks
            .Where(s =>
                // 1. 价格筛选：过滤低价垃圾股和高价股
                s.CurrentPrice >= MinPrice && s.CurrentPrice <= MaxPrice &&
                
                // 2. 涨跌幅筛选：避免下跌股和追高风险（当天涨幅不超过7%）
                s.ChangePercent >= MinChangePercent && s.ChangePercent <= MaxChangePercent &&
                
                // 3. 换手率筛选：确保有活跃度但避免庄家出货（2%-12%）
                s.TurnoverRate >= MinTurnoverRate && s.TurnoverRate <= MaxTurnoverRate &&
                
                // 4. 成交量筛选：确保流动性充足
                s.Volume >= MinVolume &&
                
                // 5. 排除ST、*ST、退市风险股票
                !s.Name.Contains("ST", StringComparison.OrdinalIgnoreCase) &&
                !s.Name.Contains("*", StringComparison.OrdinalIgnoreCase) &&
                !s.Name.Contains("退", StringComparison.OrdinalIgnoreCase) &&
                
                // 6. 排除停牌股票（涨跌幅为0且成交量为0）
                !(s.ChangePercent == 0 && s.Volume == 0) &&
                
                // 7. 行业风险过滤：排除高风险行业
                !IsHighRiskIndustry(s.Name)
            )
            .ToList();
    }

    /// <summary>
    /// 应用深度筛选规则（基本面、技术面深度检查、新闻过滤）
    /// 使用并行处理提高性能，并收集详细统计信息
    /// </summary>
    private async Task<(List<Stock> PassedStocks, FilterStatistics Statistics)> ApplyDeepFilterRulesAsync(
        List<Stock> stocks,
        IAutoFilterService autoFilterService,
        CancellationToken cancellationToken)
    {
        var passedStocks = new System.Collections.Concurrent.ConcurrentBag<Stock>();
        var statistics = new FilterStatistics
        {
            TotalChecked = stocks.Count
        };
        
        // 使用线程安全的计数器
        var fundamentalCounters = new ConcurrentDictionary<FundamentalFilterFailureType, int>();
        var technicalCounters = new ConcurrentDictionary<TechnicalFilterFailureType, int>();
        var newsNegativeCount = new ConcurrentDictionary<string, int>(); // 使用字典来计数
        var filteredIndustries = new ConcurrentDictionary<string, int>(); // 记录被过滤的行业
        var passedFundamentalCount = 0;
        var passedTechnicalCount = 0;
        var passedNewsCount = 0;
        var passedAllCount = 0;
        
        // 限制并发数量，避免过多并发请求
        var semaphore = new SemaphoreSlim(10); // 最多10个并发检查
        var tasks = new List<Task>();

        foreach (var stock in stocks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    // 为每个并行任务创建新的服务范围，避免DbContext线程冲突
                    using var taskScope = _serviceProvider.CreateScope();
                    var taskAutoFilterService = taskScope.ServiceProvider.GetRequiredService<IAutoFilterService>();

                    // 1. 检查基本面条件
                    var fundamentalResult = await taskAutoFilterService.CheckFundamentalConditionsAsync(stock.Code);
                    if (!fundamentalResult.Passed)
                    {
                        _logger.LogDebug("股票 {StockCode} 基本面不符合: {Reason}", stock.Code, fundamentalResult.Reason);
                        // 更新统计
                        fundamentalCounters.AddOrUpdate(fundamentalResult.FailureType, 1, (key, oldValue) => oldValue + 1);
                        
                        // 如果是行业过滤，记录行业名称
                        if (fundamentalResult.FailureType == FundamentalFilterFailureType.Industry &&
                            fundamentalResult.Details.TryGetValue("Industry", out var industryObj))
                        {
                            var industryName = industryObj?.ToString() ?? "未知";
                            filteredIndustries.AddOrUpdate(industryName, 1, (key, oldValue) => oldValue + 1);
                        }
                        return;
                    }
                    Interlocked.Increment(ref passedFundamentalCount);

                    // 2. 检查技术面条件（深度检查：均线、MACD、量价关系、波动率等）
                    var technicalResult = await taskAutoFilterService.CheckTechnicalConditionsAsync(stock.Code);
                    if (!technicalResult.Passed)
                    {
                        _logger.LogDebug("股票 {StockCode} 技术面不符合: {Reason}", stock.Code, technicalResult.Reason);
                        // 更新统计
                        technicalCounters.AddOrUpdate(technicalResult.FailureType, 1, (key, oldValue) => oldValue + 1);
                        return;
                    }
                    Interlocked.Increment(ref passedTechnicalCount);

                    // 3. 检查公告和新闻条件
                    var newsResult = await taskAutoFilterService.CheckNewsConditionsAsync(stock.Code);
                    if (!newsResult.Passed)
                    {
                        _logger.LogDebug("股票 {StockCode} 新闻不符合: {Reason}", stock.Code, newsResult.Reason);
                        // 更新统计
                        newsNegativeCount.AddOrUpdate("count", 1, (key, oldValue) => oldValue + 1);
                        return;
                    }
                    Interlocked.Increment(ref passedNewsCount);

                    // 所有条件都通过
                    passedStocks.Add(stock);
                    Interlocked.Increment(ref passedAllCount);
                    _logger.LogDebug("股票 {StockCode} 通过深度筛选", stock.Code);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "检查股票 {StockCode} 深度筛选条件时发生错误，跳过", stock.Code);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        
        // 汇总统计信息
        statistics.PassedFundamental = passedFundamentalCount;
        statistics.PassedTechnical = passedTechnicalCount;
        statistics.PassedNews = passedNewsCount;
        statistics.PassedAll = passedAllCount;
        statistics.FilteredByFundamental = statistics.TotalChecked - statistics.PassedFundamental;
        statistics.FilteredByTechnical = statistics.PassedFundamental - statistics.PassedTechnical;
        statistics.FilteredByNews = statistics.PassedTechnical - statistics.PassedNews;
        statistics.FilteredByNewsNegative = newsNegativeCount.TryGetValue("count", out var newsCount) ? newsCount : 0;
        
        // 汇总基本面详细统计
        foreach (var kvp in fundamentalCounters)
        {
            switch (kvp.Key)
            {
                case FundamentalFilterFailureType.DataMissing:
                    statistics.FilteredByFundamentalDataMissing = kvp.Value;
                    break;
                case FundamentalFilterFailureType.NetProfit:
                    statistics.FilteredByNetProfit = kvp.Value;
                    break;
                case FundamentalFilterFailureType.ROE:
                    statistics.FilteredByROE = kvp.Value;
                    break;
                case FundamentalFilterFailureType.GrossProfitMargin:
                    statistics.FilteredByGrossProfitMargin = kvp.Value;
                    break;
                case FundamentalFilterFailureType.RevenueGrowth:
                    statistics.FilteredByRevenueGrowth = kvp.Value;
                    break;
                case FundamentalFilterFailureType.ST:
                    statistics.FilteredByST = kvp.Value;
                    break;
                case FundamentalFilterFailureType.AssetLiabilityRatio:
                    statistics.FilteredByAssetLiabilityRatio = kvp.Value;
                    break;
                case FundamentalFilterFailureType.CurrentRatio:
                    statistics.FilteredByCurrentRatio = kvp.Value;
                    break;
                case FundamentalFilterFailureType.Industry:
                    statistics.FilteredByIndustry = kvp.Value;
                    break;
            }
        }
        
        // 汇总技术面详细统计
        foreach (var kvp in technicalCounters)
        {
            switch (kvp.Key)
            {
                case TechnicalFilterFailureType.DataMissing:
                    statistics.FilteredByTechnicalDataMissing = kvp.Value;
                    break;
                case TechnicalFilterFailureType.ReversalSignal:
                    statistics.FilteredByReversalSignal = kvp.Value;
                    break;
                case TechnicalFilterFailureType.Volume:
                    statistics.FilteredByVolume = kvp.Value;
                    break;
                case TechnicalFilterFailureType.TurnoverRate:
                    statistics.FilteredByTurnoverRate = kvp.Value;
                    break;
                case TechnicalFilterFailureType.ChangePercent:
                    statistics.FilteredByChangePercent = kvp.Value;
                    break;
                case TechnicalFilterFailureType.Volatility:
                    statistics.FilteredByVolatility = kvp.Value;
                    break;
            }
        }
        
        // 更新行业统计
        statistics.FilteredIndustries = filteredIndustries.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        
        return (passedStocks.ToList(), statistics);
    }

    /// <summary>
    /// 判断是否为高风险行业（地产、煤炭等）
    /// </summary>
    private static bool IsHighRiskIndustry(string stockName)
    {
        // 过滤地产开发、煤炭等政策风险大的行业
        var highRiskKeywords = new[] 
        { 
            "地产", "房地产", "置业", "房产",
            "煤炭", "煤业", "矿业"
        };
        
        return highRiskKeywords.Any(keyword => 
            stockName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 丰富股票数据（行业、人气榜）- 使用并行处理
    /// </summary>
    private async Task<List<EnrichedStock>> EnrichStockDataAsync(
        List<Stock> stocks,
        IIndustryService industryService,
        IMarketService marketService,
        CancellationToken cancellationToken)
    {
        var enrichedStocks = new System.Collections.Concurrent.ConcurrentBag<EnrichedStock>();
        
        // 限制并发数量，避免过多并发请求
        var semaphore = new SemaphoreSlim(20); // 最多20个并发请求
        var tasks = new List<Task>();

        foreach (var stock in stocks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    // 为每个并行任务创建新的服务范围，避免DbContext线程冲突
                    using var taskScope = _serviceProvider.CreateScope();
                    var taskIndustryService = taskScope.ServiceProvider.GetRequiredService<IIndustryService>();
                    var taskMarketService = taskScope.ServiceProvider.GetRequiredService<IMarketService>();
                    
                    var enrichedStock = new EnrichedStock
                    {
                        Stock = stock,
                        IndustryInfo = null,
                        HotRank = string.Empty
                    };

                    // 获取行业信息（已禁用）
                    // 由于数据源限制，不再获取行业信息
                    enrichedStock.IndustryInfo = new IndustryInfoResult 
                    { 
                        IndustryName = "未获取",
                        InfoText = "行业数据获取已禁用"
                    };

                    // 获取人气榜信息（带超时）
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                        enrichedStock.HotRank = await taskMarketService.GetHotRankFromAKShareAsync(stock.Code);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "获取股票 {StockCode} 的人气榜信息失败", stock.Code);
                    }

                    enrichedStocks.Add(enrichedStock);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "丰富股票 {StockCode} 数据时发生错误", stock.Code);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return enrichedStocks.ToList();
    }

    /// <summary>
    /// 使用AI对股票进行评分 - 使用并行处理
    /// </summary>
    private async Task<List<ScoredStock>> ScoreStocksWithAIAsync(
        List<EnrichedStock> stocks,
        IAIService aiService,
        CancellationToken cancellationToken)
    {
        var scoredStocks = new System.Collections.Concurrent.ConcurrentBag<ScoredStock>();
        
        // 限制AI并发数量，避免过多并发请求
        var semaphore = new SemaphoreSlim(10); // 最多10个并发AI请求
        var tasks = new List<Task>();

        foreach (var enrichedStock in stocks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    // 为每个并行任务创建新的服务范围，避免DbContext线程冲突
                    using var taskScope = _serviceProvider.CreateScope();
                    var taskAIService = taskScope.ServiceProvider.GetRequiredService<IAIService>();
                    
                    var stock = enrichedStock.Stock;
                    
                    // 构建AI分析上下文
                    var context = BuildAIContext(enrichedStock);
                    
                    // 调用AI进行评分（带超时）
                    var prompt = $@"请对以下股票进行评分（0-10分），只返回一个数字分数，不要其他内容。

股票代码: {stock.Code}
股票名称: {stock.Name}
当前价格: {stock.CurrentPrice}
涨跌幅: {stock.ChangePercent}%
换手率: {stock.TurnoverRate}%
成交量: {stock.Volume}

{context}

请综合考虑技术面、资金面、行业地位、市场热度等因素，给出0-10分的评分。只返回数字，例如：8";

                    string aiResponse;
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                        aiResponse = await taskAIService.ExecutePromptAsync(null, prompt, null, null);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("股票 {StockCode} AI评分超时", stock.Code);
                        aiResponse = "0";
                    }

                    // 尝试从AI响应中提取分数
                    var score = ExtractScoreFromAIResponse(aiResponse);
                    
                    scoredStocks.Add(new ScoredStock
                    {
                        Stock = stock,
                        IndustryInfo = enrichedStock.IndustryInfo,
                        HotRank = enrichedStock.HotRank,
                        AIScore = score
                    });

                    _logger.LogDebug("股票 {StockCode} AI评分: {Score}", stock.Code, score);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "对股票 {StockCode} 进行AI评分时发生错误", enrichedStock.Stock.Code);
                    // 评分失败时，给一个默认低分
                    scoredStocks.Add(new ScoredStock
                    {
                        Stock = enrichedStock.Stock,
                        IndustryInfo = enrichedStock.IndustryInfo,
                        HotRank = enrichedStock.HotRank,
                        AIScore = 0
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return scoredStocks.ToList();
    }

    /// <summary>
    /// 构建AI分析上下文
    /// </summary>
    private string BuildAIContext(EnrichedStock enrichedStock)
    {
        var sb = new System.Text.StringBuilder();
        
        if (enrichedStock.IndustryInfo != null)
        {
            sb.AppendLine($"行业信息: {enrichedStock.IndustryInfo.IndustryName ?? "未知"}");
            if (!string.IsNullOrEmpty(enrichedStock.IndustryInfo.InfoText))
            {
                sb.AppendLine($"行业详情: {enrichedStock.IndustryInfo.InfoText.Substring(0, Math.Min(200, enrichedStock.IndustryInfo.InfoText.Length))}...");
            }
        }

        if (!string.IsNullOrEmpty(enrichedStock.HotRank))
        {
            sb.AppendLine($"市场热度: {enrichedStock.HotRank.Substring(0, Math.Min(200, enrichedStock.HotRank.Length))}...");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从AI响应中提取分数
    /// </summary>
    private int ExtractScoreFromAIResponse(string aiResponse)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
        {
            return 0;
        }

        // 尝试提取数字
        var match = System.Text.RegularExpressions.Regex.Match(aiResponse, @"\b([0-9]|10)\b");
        if (match.Success && int.TryParse(match.Value, out var score))
        {
            return Math.Clamp(score, 0, 10);
        }

        return 0;
    }

    /// <summary>
    /// 丰富后的股票数据
    /// </summary>
    private class EnrichedStock
    {
        public Stock Stock { get; set; } = null!;
        public IndustryInfoResult? IndustryInfo { get; set; }
        public string HotRank { get; set; } = string.Empty;
    }

    /// <summary>
    /// 评分后的股票数据
    /// </summary>
    private class ScoredStock
    {
        public Stock Stock { get; set; } = null!;
        public IndustryInfoResult? IndustryInfo { get; set; }
        public string HotRank { get; set; } = string.Empty;
        public int AIScore { get; set; }
    }
}

