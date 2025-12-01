using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace StockAnalyse.Api.Services;

/// <summary>
/// 自动选股服务实现
/// </summary>
public class AutoSelectionService : IAutoSelectionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoSelectionService> _logger;

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
        using var scope = _serviceProvider.CreateScope();
        var stockDataService = scope.ServiceProvider.GetRequiredService<IStockDataService>();
        var industryService = scope.ServiceProvider.GetRequiredService<IIndustryService>();
        var marketService = scope.ServiceProvider.GetRequiredService<IMarketService>();
        var aiService = scope.ServiceProvider.GetRequiredService<IAIService>();

        try
        {
            _logger.LogInformation("开始执行自动选股流程...");

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

            // 步骤2: 应用技术面和资金面筛选规则
            _logger.LogInformation("步骤2: 应用技术面和资金面筛选规则...");
            var filteredStocks = ApplyFilterRules(allStocks);
            _logger.LogInformation("筛选后剩余 {Count} 只股票", filteredStocks.Count);

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

            _logger.LogInformation("自动选股任务完成 - 筛选出 {Count} 只股票", highScoreStocks.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行自动选股任务时发生错误");
            return new AutoSelectionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 应用技术面和资金面筛选规则
    /// </summary>
    private List<Stock> ApplyFilterRules(List<Stock> stocks)
    {
        return stocks
            .Where(s =>
                // 1. 价格筛选：过滤低价垃圾股和高价股
                s.CurrentPrice >= MinPrice && s.CurrentPrice <= MaxPrice &&
                
                // 2. 涨跌幅筛选：避免下跌股和追高风险
                s.ChangePercent >= MinChangePercent && s.ChangePercent <= MaxChangePercent &&
                
                // 3. 换手率筛选：确保有活跃度但避免庄家出货
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

                    // 获取行业信息（带超时）
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                        enrichedStock.IndustryInfo = await taskIndustryService.GetIndustryInfoFromAKShareAsync(stock.Code);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "获取股票 {StockCode} 的行业信息失败", stock.Code);
                    }

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

