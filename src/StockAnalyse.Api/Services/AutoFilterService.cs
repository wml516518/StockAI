using Microsoft.Extensions.Logging;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.RegularExpressions;

namespace StockAnalyse.Api.Services;

/// <summary>
/// 自动筛选股票服务
/// 实现基本面、技术面、公告新闻、社交舆情等多维度筛选
/// </summary>
public class AutoFilterService : IAutoFilterService
{
    private readonly IStockDataService _stockDataService;
    private readonly INewsService _newsService;
    private readonly IIndustryService _industryService;
    private readonly ILogger<AutoFilterService> _logger;

    // 负面关键词列表
    private static readonly List<string> NegativeKeywords = new()
    {
        "业绩下滑", "业绩下降", "业绩亏损", "亏损预告", "预亏", "业绩预警",
        "高管减持", "大股东减持", "减持计划", "减持公告",
        "大额解禁", "限售股解禁", "解禁公告",
        "监管处罚", "监管调查", "立案调查", "证监会", "交易所", "处罚决定",
        "下调评级", "评级下调", "目标价下调", "卖出评级", "减持评级"
    };

    // 需要过滤的行业关键词（更精确的匹配）
    // 注意：只过滤明确的高风险行业，避免误杀
    private static readonly List<string> ExcludedIndustryKeywords = new()
    {
        "ST", "*ST", "退市", "地产开发", "房地产开发" // 移除"房地产"和"煤炭"，因为太宽泛
    };
    
    // 需要严格匹配的行业关键词（必须完全匹配或作为独立词出现）
    private static readonly List<string> StrictExcludedKeywords = new()
    {
        "煤炭开采", "煤炭采选", "煤炭洗选" // 只过滤明确的煤炭开采相关
    };

    public AutoFilterService(
        IStockDataService stockDataService,
        INewsService newsService,
        IIndustryService industryService,
        ILogger<AutoFilterService> logger)
    {
        _stockDataService = stockDataService;
        _newsService = newsService;
        _industryService = industryService;
        _logger = logger;
    }

    /// <summary>
    /// 自动筛选股票（综合基本面、技术面、公告新闻等条件）
    /// </summary>
    public async Task<AutoFilterResult> FilterStocksAsync(
        List<string>? stockCodes = null,
        bool enableSentimentFilter = false)
    {
        var result = new AutoFilterResult();
        var statistics = new FilterStatistics();
        var filteredIndustries = new Dictionary<string, int>(); // 记录被过滤的行业

        // 如果没有提供股票代码列表，从全市场获取
        if (stockCodes == null || stockCodes.Count == 0)
        {
            _logger.LogInformation("未提供股票代码列表，从全市场获取股票");
            var allStocks = await _stockDataService.FetchAllStocksFromTencentAsync(null, 2000);
            stockCodes = allStocks.Select(s => s.Code).ToList();
        }

        statistics.TotalChecked = stockCodes.Count;
        _logger.LogInformation("开始自动筛选股票，共 {Count} 只股票", stockCodes.Count);

        foreach (var stockCode in stockCodes)
        {
            try
            {
                // 1. 基本面筛选
                var fundamentalResult = await CheckFundamentalConditionsAsync(stockCode);
                if (!fundamentalResult.Passed)
                {
                    result.FilteredReasons[stockCode] = $"基本面不符合: {fundamentalResult.Reason}";
                    statistics.FilteredByFundamental++;
                    
                    // 根据失败类型更新详细统计
                    switch (fundamentalResult.FailureType)
                    {
                        case FundamentalFilterFailureType.DataMissing:
                            statistics.FilteredByFundamentalDataMissing++;
                            break;
                        case FundamentalFilterFailureType.NetProfit:
                            statistics.FilteredByNetProfit++;
                            break;
                        case FundamentalFilterFailureType.ROE:
                            statistics.FilteredByROE++;
                            break;
                        case FundamentalFilterFailureType.GrossProfitMargin:
                            statistics.FilteredByGrossProfitMargin++;
                            break;
                        case FundamentalFilterFailureType.RevenueGrowth:
                            statistics.FilteredByRevenueGrowth++;
                            break;
                        case FundamentalFilterFailureType.ST:
                            statistics.FilteredByST++;
                            break;
                        case FundamentalFilterFailureType.AssetLiabilityRatio:
                            statistics.FilteredByAssetLiabilityRatio++;
                            break;
                        case FundamentalFilterFailureType.CurrentRatio:
                            statistics.FilteredByCurrentRatio++;
                            break;
                        case FundamentalFilterFailureType.Industry:
                            statistics.FilteredByIndustry++;
                            // 记录被过滤的行业
                            if (fundamentalResult.Details.TryGetValue("Industry", out var industryObj))
                            {
                                var industryName = industryObj?.ToString() ?? "未知";
                                if (filteredIndustries.ContainsKey(industryName))
                                {
                                    filteredIndustries[industryName]++;
                                }
                                else
                                {
                                    filteredIndustries[industryName] = 1;
                                }
                            }
                            break;
                    }
                    continue;
                }
                statistics.PassedFundamental++;

                // 2. 技术面筛选
                var technicalResult = await CheckTechnicalConditionsAsync(stockCode);
                if (!technicalResult.Passed)
                {
                    result.FilteredReasons[stockCode] = $"技术面不符合: {technicalResult.Reason}";
                    statistics.FilteredByTechnical++;
                    
                    // 根据失败类型更新详细统计
                    switch (technicalResult.FailureType)
                    {
                        case TechnicalFilterFailureType.DataMissing:
                            statistics.FilteredByTechnicalDataMissing++;
                            break;
                        case TechnicalFilterFailureType.ReversalSignal:
                            statistics.FilteredByReversalSignal++;
                            break;
                        case TechnicalFilterFailureType.Volume:
                            statistics.FilteredByVolume++;
                            break;
                        case TechnicalFilterFailureType.TurnoverRate:
                            statistics.FilteredByTurnoverRate++;
                            break;
                        case TechnicalFilterFailureType.ChangePercent:
                            statistics.FilteredByChangePercent++;
                            break;
                        case TechnicalFilterFailureType.Volatility:
                            statistics.FilteredByVolatility++;
                            break;
                    }
                    continue;
                }
                statistics.PassedTechnical++;

                // 3. 公告新闻筛选
                var newsResult = await CheckNewsConditionsAsync(stockCode);
                if (!newsResult.Passed)
                {
                    result.FilteredReasons[stockCode] = $"公告新闻负面: {newsResult.Reason}";
                    statistics.FilteredByNews++;
                    statistics.FilteredByNewsNegative++;
                    continue;
                }
                statistics.PassedNews++;

                // 4. 社交舆情筛选（可选）
                if (enableSentimentFilter)
                {
                    var sentimentResult = await CheckSentimentConditionsAsync(stockCode);
                    if (!sentimentResult.Passed)
                    {
                        result.FilteredReasons[stockCode] = $"社交舆情负面: {sentimentResult.Reason}";
                        continue;
                    }
                }

                // 所有条件都通过
                result.PassedStockCodes.Add(stockCode);
                statistics.PassedAll++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "筛选股票 {StockCode} 时发生异常，跳过", stockCode);
                result.FilteredReasons[stockCode] = $"筛选异常: {ex.Message}";
            }
        }

        // 更新行业统计
        statistics.FilteredIndustries = filteredIndustries;
        result.Statistics = statistics;
        
        // 输出详细统计信息
        _logger.LogInformation("=== 自动筛选完成统计 ===");
        _logger.LogInformation("总数: {Total}, 最终通过: {All}", statistics.TotalChecked, statistics.PassedAll);
        _logger.LogInformation("基本面: 通过={Passed}, 失败={Failed}", statistics.PassedFundamental, statistics.FilteredByFundamental);
        _logger.LogInformation("  - 数据缺失: {DataMissing}", statistics.FilteredByFundamentalDataMissing);
        _logger.LogInformation("  - 净利润<=0: {NetProfit}", statistics.FilteredByNetProfit);
        _logger.LogInformation("  - ROE<=6%: {ROE}", statistics.FilteredByROE);
        _logger.LogInformation("  - 毛利率<=15%: {GrossMargin}", statistics.FilteredByGrossProfitMargin);
        _logger.LogInformation("  - 营收同比<=0: {RevenueGrowth}", statistics.FilteredByRevenueGrowth);
        _logger.LogInformation("  - ST类股票: {ST}", statistics.FilteredByST);
        _logger.LogInformation("  - 资产负债率>=70%: {AssetLiability}", statistics.FilteredByAssetLiabilityRatio);
        _logger.LogInformation("  - 流动比率<=1: {CurrentRatio}", statistics.FilteredByCurrentRatio);
        _logger.LogInformation("  - 行业不符合: {Industry} (共{Count}个不同行业)", statistics.FilteredByIndustry, filteredIndustries.Count);
        
        // 输出被过滤的行业详情（按数量排序，只显示前10个）
        if (filteredIndustries.Count > 0)
        {
            var topFilteredIndustries = filteredIndustries.OrderByDescending(kvp => kvp.Value).Take(10).ToList();
            _logger.LogInformation("  - 被过滤行业详情（Top 10）:");
            foreach (var kvp in topFilteredIndustries)
            {
                _logger.LogInformation("    * {IndustryName}: {Count}只", kvp.Key, kvp.Value);
            }
        }
        _logger.LogInformation("技术面: 通过={Passed}, 失败={Failed}", statistics.PassedTechnical, statistics.FilteredByTechnical);
        _logger.LogInformation("  - 数据缺失: {DataMissing}", statistics.FilteredByTechnicalDataMissing);
        _logger.LogInformation("  - 未满足反转信号: {ReversalSignal}", statistics.FilteredByReversalSignal);
        _logger.LogInformation("  - 成交量不足: {Volume}", statistics.FilteredByVolume);
        _logger.LogInformation("  - 换手率不在2%-12%: {TurnoverRate}", statistics.FilteredByTurnoverRate);
        _logger.LogInformation("  - 涨幅>7%: {ChangePercent}", statistics.FilteredByChangePercent);
        _logger.LogInformation("  - 波动率>=6%: {Volatility}", statistics.FilteredByVolatility);
        _logger.LogInformation("新闻: 通过={Passed}, 失败={Failed} (负面新闻: {Negative})", 
            statistics.PassedNews, statistics.FilteredByNews, statistics.FilteredByNewsNegative);

        return result;
    }

    /// <summary>
    /// 检查股票是否符合基本面条件
    /// </summary>
    public async Task<FundamentalFilterResult> CheckFundamentalConditionsAsync(string stockCode)
    {
        var result = new FundamentalFilterResult { Passed = true };
        var details = new Dictionary<string, object>();

        try
        {
            // 获取基本面信息
            var fundamentalInfo = await _stockDataService.GetFundamentalInfoAsync(stockCode, forceRefresh: false);
            if (fundamentalInfo == null)
            {
                result.Passed = false;
                result.Reason = "无法获取基本面数据";
                result.FailureType = FundamentalFilterFailureType.DataMissing;
                return result;
            }

            // 1. 盈利能力检查
            // 最近4个季度累计净利润为正（TTM > 0）
            if (fundamentalInfo.NetProfit.HasValue && fundamentalInfo.NetProfit.Value <= 0)
            {
                result.Passed = false;
                result.Reason = $"净利润为负或零: {fundamentalInfo.NetProfit.Value}万元";
                result.FailureType = FundamentalFilterFailureType.NetProfit;
                details["NetProfit"] = fundamentalInfo.NetProfit.Value;
                return result;
            }
            details["NetProfit"] = fundamentalInfo.NetProfit?.ToString("F2") ?? "N/A";

            // ROE（TTM）> 6%
            if (fundamentalInfo.ROE.HasValue && fundamentalInfo.ROE.Value <= 6)
            {
                result.Passed = false;
                result.Reason = $"ROE过低: {fundamentalInfo.ROE.Value:F2}% (要求>6%)";
                result.FailureType = FundamentalFilterFailureType.ROE;
                details["ROE"] = fundamentalInfo.ROE.Value;
                return result;
            }
            details["ROE"] = fundamentalInfo.ROE?.ToString("F2") ?? "N/A";

            // 毛利率 > 15%
            if (fundamentalInfo.GrossProfitMargin.HasValue && fundamentalInfo.GrossProfitMargin.Value <= 15)
            {
                result.Passed = false;
                result.Reason = $"毛利率过低: {fundamentalInfo.GrossProfitMargin.Value:F2}% (要求>15%)";
                result.FailureType = FundamentalFilterFailureType.GrossProfitMargin;
                details["GrossProfitMargin"] = fundamentalInfo.GrossProfitMargin.Value;
                return result;
            }
            details["GrossProfitMargin"] = fundamentalInfo.GrossProfitMargin?.ToString("F2") ?? "N/A";

            // 2. 稳定性检查
            // 营收同比 > 0
            if (fundamentalInfo.RevenueGrowthRate.HasValue && fundamentalInfo.RevenueGrowthRate.Value <= 0)
            {
                result.Passed = false;
                result.Reason = $"营收同比为负或零: {fundamentalInfo.RevenueGrowthRate.Value:F2}%";
                result.FailureType = FundamentalFilterFailureType.RevenueGrowth;
                details["RevenueGrowthRate"] = fundamentalInfo.RevenueGrowthRate.Value;
                return result;
            }
            details["RevenueGrowthRate"] = fundamentalInfo.RevenueGrowthRate?.ToString("F2") ?? "N/A";

            // 经营现金流为正（需要从其他数据源获取，这里先跳过，后续可以扩展）
            // TODO: 从Python服务获取经营现金流数据

            // 三年未出现重大财务造假或ST记录（需要历史数据，这里先检查股票名称）
            var stock = await _stockDataService.GetRealTimeQuoteAsync(stockCode);
            if (stock != null)
            {
                if (stock.Name.Contains("ST") || stock.Name.Contains("*ST") || stock.Name.Contains("退市"))
                {
                    result.Passed = false;
                    result.Reason = $"股票为ST类: {stock.Name}";
                    result.FailureType = FundamentalFilterFailureType.ST;
                    details["StockName"] = stock.Name;
                    return result;
                }
                details["StockName"] = stock.Name;
            }

            // 3. 负债水平检查
            // 资产负债率 < 70%
            if (fundamentalInfo.AssetLiabilityRatio.HasValue && fundamentalInfo.AssetLiabilityRatio.Value >= 70)
            {
                result.Passed = false;
                result.Reason = $"资产负债率过高: {fundamentalInfo.AssetLiabilityRatio.Value:F2}% (要求<70%)";
                result.FailureType = FundamentalFilterFailureType.AssetLiabilityRatio;
                details["AssetLiabilityRatio"] = fundamentalInfo.AssetLiabilityRatio.Value;
                return result;
            }
            details["AssetLiabilityRatio"] = fundamentalInfo.AssetLiabilityRatio?.ToString("F2") ?? "N/A";

            // 流动比率 > 1
            if (fundamentalInfo.CurrentRatio.HasValue && fundamentalInfo.CurrentRatio.Value <= 1)
            {
                result.Passed = false;
                result.Reason = $"流动比率过低: {fundamentalInfo.CurrentRatio.Value:F2} (要求>1)";
                result.FailureType = FundamentalFilterFailureType.CurrentRatio;
                details["CurrentRatio"] = fundamentalInfo.CurrentRatio.Value;
                return result;
            }
            details["CurrentRatio"] = fundamentalInfo.CurrentRatio?.ToString("F2") ?? "N/A";

            // 4. 行业过滤（已移除）
            // 由于数据源限制，不再进行基于行业数据的过滤
            // 仅保留基于名称的简单过滤（在AutoSelectionService.ApplyQuickFilterRules中实现）
            _logger.LogDebug("股票 {StockCode} 跳过行业数据过滤（已禁用）", stockCode);
            details["Industry"] = "未检查";

            result.Details = details;
            result.Reason = "基本面条件全部通过";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查股票 {StockCode} 基本面条件时发生异常", stockCode);
            result.Passed = false;
            result.Reason = $"检查异常: {ex.Message}";
            result.FailureType = FundamentalFilterFailureType.Exception;
        }

        return result;
    }

    /// <summary>
    /// 检查股票是否符合技术面条件
    /// </summary>
    public async Task<TechnicalFilterResult> CheckTechnicalConditionsAsync(string stockCode)
    {
        var result = new TechnicalFilterResult { Passed = false };
        var details = new Dictionary<string, object>();

        try
        {
            // 获取实时行情
            var stock = await _stockDataService.GetRealTimeQuoteAsync(stockCode);
            if (stock == null)
            {
                result.Reason = "无法获取实时行情";
                result.FailureType = TechnicalFilterFailureType.DataMissing;
                return result;
            }

            // 获取近3个月历史数据
            var endDate = DateTime.Now;
            var startDate = endDate.AddMonths(-3);
            var historyData = await _stockDataService.GetDailyDataAsync(stockCode, startDate, endDate);
            
            if (historyData == null || historyData.Count < 20)
            {
                result.Reason = "历史数据不足（至少需要20个交易日）";
                result.FailureType = TechnicalFilterFailureType.DataMissing;
                return result;
            }

            var sortedHistory = historyData.OrderBy(h => h.TradeDate).ToList();
            var recentHistory = sortedHistory.TakeLast(20).ToList(); // 最近20个交易日

            // 1. 一周反转信号检查
            bool hasReversalSignal = false;
            string reversalReason = "";

            // 计算5日均线
            if (recentHistory.Count >= 5)
            {
                var last5Days = recentHistory.TakeLast(5).ToList();
                var ma5 = last5Days.Average(h => h.Close);
                var previous5Days = recentHistory.Skip(recentHistory.Count - 6).Take(5).ToList();
                var previousMa5 = previous5Days.Average(h => h.Close);

                // 5日均线向上拐头，且股价站上5日线
                if (ma5 > previousMa5 && stock.CurrentPrice > ma5)
                {
                    hasReversalSignal = true;
                    reversalReason = "5日均线向上拐头且股价站上5日线";
                    details["MA5Signal"] = $"当前价{stock.CurrentPrice:F2} > MA5{ma5:F2}，MA5上升";
                }
            }

            // MACD金叉检查（需要计算MACD）
            if (!hasReversalSignal)
            {
                try
                {
                    var (macd, signal, histogram) = await _stockDataService.CalculateMACDAsync(stockCode);
                    if (macd > signal && histogram > 0)
                    {
                        hasReversalSignal = true;
                        reversalReason = "MACD金叉（DIF上穿DEA）";
                        details["MACDSignal"] = $"MACD={macd:F4}, Signal={signal:F4}, Histogram={histogram:F4}";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "计算MACD失败，跳过MACD检查");
                }
            }

            // 近5天放量检查
            if (!hasReversalSignal && recentHistory.Count >= 10)
            {
                var last5Days = recentHistory.TakeLast(5).ToList();
                var previous10Days = recentHistory.Skip(recentHistory.Count - 15).Take(10).ToList();
                
                var avgVolume5 = last5Days.Average(h => h.Volume);
                var avgVolume10 = previous10Days.Average(h => h.Volume);
                var maxVolume = recentHistory.Max(h => h.Volume);

                // 近5天放量但不超过前期巨量
                if (avgVolume5 > avgVolume10 * 1.2m && avgVolume5 < maxVolume * 0.8m)
                {
                    hasReversalSignal = true;
                    reversalReason = "近5天放量（未超过前期巨量）";
                    details["VolumeSignal"] = $"5日均量={avgVolume5:F0} > 10日均量*1.2={avgVolume10 * 1.2m:F0}";
                }
            }

            if (!hasReversalSignal)
            {
                result.Reason = "未满足一周反转信号条件";
                result.FailureType = TechnicalFilterFailureType.ReversalSignal;
                return result;
            }
            details["ReversalSignal"] = reversalReason;

            // 2. 量价条件检查
            // 今日成交量 > 最近5日平均量
            if (recentHistory.Count >= 5)
            {
                var last5Days = recentHistory.TakeLast(5).ToList();
                var avgVolume5 = last5Days.Average(h => h.Volume);
                
                if (stock.Volume <= avgVolume5)
                {
                    result.Reason = $"今日成交量不足: {stock.Volume:F0} <= 5日均量{avgVolume5:F0}";
                    result.FailureType = TechnicalFilterFailureType.Volume;
                    return result;
                }
                details["Volume"] = $"今日{stock.Volume:F0} > 5日均量{avgVolume5:F0}";
            }

            // 换手率 2%~12%
            if (stock.TurnoverRate < 2 || stock.TurnoverRate > 12)
            {
                result.Reason = $"换手率不在合理范围: {stock.TurnoverRate:F2}% (要求2%-12%)";
                result.FailureType = TechnicalFilterFailureType.TurnoverRate;
                return result;
            }
            details["TurnoverRate"] = $"{stock.TurnoverRate:F2}%";

            // 3. 波动风险过滤
            // 当天涨幅不要超过7%
            if (stock.ChangePercent > 7)
            {
                result.Reason = $"当天涨幅过大: {stock.ChangePercent:F2}% (要求<=7%)";
                result.FailureType = TechnicalFilterFailureType.ChangePercent;
                return result;
            }
            details["ChangePercent"] = $"{stock.ChangePercent:F2}%";

            // 波动率（ATR/price）< 6%
            if (recentHistory.Count >= 14)
            {
                var atr = CalculateATR(recentHistory, 14);
                var volatility = (atr / stock.CurrentPrice) * 100;
                
                if (volatility >= 6)
                {
                    result.Reason = $"波动率过高: {volatility:F2}% (要求<6%)";
                    result.FailureType = TechnicalFilterFailureType.Volatility;
                    return result;
                }
                details["Volatility"] = $"{volatility:F2}%";
            }

            result.Passed = true;
            result.Reason = "技术面条件全部通过";
            result.Details = details;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查股票 {StockCode} 技术面条件时发生异常", stockCode);
            result.Reason = $"检查异常: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// 检查股票公告和新闻是否有负面信息
    /// </summary>
    public async Task<NewsFilterResult> CheckNewsConditionsAsync(string stockCode)
    {
        var result = new NewsFilterResult { Passed = true };

        try
        {
            // 获取最近30天的新闻
            var newsList = await _newsService.GetNewsByStockAsync(stockCode, forceRefresh: false);
            
            if (newsList == null || newsList.Count == 0)
            {
                result.Reason = "无新闻数据（视为通过）";
                return result;
            }

            var negativeKeywords = new List<string>();

            // 过滤最近30天的新闻
            var endDate = DateTime.Now;
            var startDate = endDate.AddDays(-30);
            var recentNews = newsList.Where(n => n.PublishTime >= startDate && n.PublishTime <= endDate)
                                     .OrderByDescending(n => n.PublishTime)
                                     .Take(50)
                                     .ToList();

            foreach (var news in recentNews)
            {
                var title = news.Title ?? "";
                var content = news.Content ?? "";
                var combinedText = $"{title} {content}";

                // 检查是否包含负面关键词
                foreach (var keyword in NegativeKeywords)
                {
                    if (combinedText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        negativeKeywords.Add(keyword);
                        result.Passed = false;
                    }
                }
            }

            if (!result.Passed)
            {
                var uniqueKeywords = negativeKeywords.Distinct().ToList();
                result.NegativeKeywords = uniqueKeywords;
                result.Reason = $"发现负面关键词: {string.Join(", ", uniqueKeywords)}";
            }
            else
            {
                result.Reason = "未发现负面新闻";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查股票 {StockCode} 新闻条件时发生异常", stockCode);
            // 新闻检查失败时，为了不误杀，视为通过
            result.Passed = true;
            result.Reason = $"检查异常（视为通过）: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// 检查社交舆情条件（可选）
    /// </summary>
    private Task<NewsFilterResult> CheckSentimentConditionsAsync(string stockCode)
    {
        var result = new NewsFilterResult { Passed = true };

        try
        {
            // TODO: 实现东方财富股吧情绪指数、微博雪球词频分析
            // 这里先返回通过，后续可以集成第三方API或爬虫服务
            result.Reason = "社交舆情检查暂未实现（视为通过）";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查股票 {StockCode} 社交舆情时发生异常", stockCode);
            result.Passed = true;
            result.Reason = $"检查异常（视为通过）: {ex.Message}";
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// 计算ATR（平均真实波幅）
    /// </summary>
    private decimal CalculateATR(List<StockHistory> history, int period)
    {
        if (history.Count < period + 1)
            return 0;

        var trueRanges = new List<decimal>();

        for (int i = 1; i < history.Count; i++)
        {
            var current = history[i];
            var previous = history[i - 1];

            var tr1 = current.High - current.Low;
            var tr2 = Math.Abs(current.High - previous.Close);
            var tr3 = Math.Abs(current.Low - previous.Close);

            var tr = Math.Max(tr1, Math.Max(tr2, tr3));
            trueRanges.Add(tr);
        }

        if (trueRanges.Count < period)
            return 0;

        var recentTR = trueRanges.TakeLast(period).ToList();
        return recentTR.Average();
    }
}

