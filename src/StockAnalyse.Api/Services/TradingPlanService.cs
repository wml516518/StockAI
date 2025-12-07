using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using StockAnalyse.Api.Services.Abstractions;
using System.Linq;

namespace StockAnalyse.Api.Services;

public class TradingPlanService : ITradingPlanService
{
    private readonly StockDbContext _context;
    private readonly IAIService _aiService;
    private readonly IStockDataService _stockDataService;
    private readonly INewsService _newsService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TradingPlanService> _logger;
    private readonly TradingPlanEventService? _eventService;

    public TradingPlanService(
        StockDbContext context,
        IAIService aiService,
        IStockDataService stockDataService,
        INewsService newsService,
        IMemoryCache cache,
        ILogger<TradingPlanService> logger,
        IServiceProvider serviceProvider)
    {
        _context = context;
        _aiService = aiService;
        _stockDataService = stockDataService;
        _newsService = newsService;
        _cache = cache;
        _logger = logger;
        // 从服务提供者获取事件服务（可能为null）
        _eventService = serviceProvider.GetService<TradingPlanEventService>();
    }

    public async Task<TradingPlanResult> GenerateTradingPlanAsync(string stockCode)
    {
        try
        {
            _logger.LogInformation("开始生成做T方案: {StockCode}", stockCode);

            // 获取股票实时行情
            var stock = await _stockDataService.GetRealTimeQuoteAsync(stockCode);
            if (stock == null)
            {
                return new TradingPlanResult
                {
                    Success = false,
                    Message = "无法获取股票行情数据"
                };
            }

            var stockName = stock.Name;
            var currentPrice = stock.CurrentPrice;
            var changePercent = stock.ChangePercent;
            var changeAmount = stock.ChangeAmount;
            var openPrice = stock.OpenPrice;
            var prevClosePrice = stock.ClosePrice;
            var highPrice = stock.HighPrice;
            var lowPrice = stock.LowPrice;
            var volume = stock.Volume;
            var turnover = stock.Turnover;
            var turnoverRate = stock.TurnoverRate;

            // 获取近20个交易日的高低点数据，用于更多维度参考
            var historicalRanges = await GetHistoricalRangesAsync(stockCode);
            var range5 = historicalRanges.FiveDay;
            var range10 = historicalRanges.TenDay;
            var range20 = historicalRanges.TwentyDay;

            // 获取技术指标数据（从AI分析缓存中获取）
            var analysisTypes = new[] { "comprehensive", "technical", "fundamental" };
            string? analysisContent = null;
            
            foreach (var analysisType in analysisTypes)
            {
                var cacheKey = $"ai_analysis_{stockCode}_{analysisType}";
                if (_cache.TryGetValue(cacheKey, out object? cachedObj) && cachedObj != null)
                {
                    // 使用反射获取Analysis属性（因为CachedAnalysisResult在AIController中定义）
                    var type = cachedObj.GetType();
                    var analysisProperty = type.GetProperty("Analysis");
                    if (analysisProperty != null)
                    {
                        analysisContent = analysisProperty.GetValue(cachedObj)?.ToString();
                        if (!string.IsNullOrWhiteSpace(analysisContent))
                        {
                            break;
                        }
                    }
                }
            }

            // 如果没有AI分析结果，尝试快速获取
            if (string.IsNullOrWhiteSpace(analysisContent))
            {
                _logger.LogWarning("股票 {StockCode} 没有AI分析结果，将使用基础数据生成做T方案", stockCode);
            }

            // 获取最新新闻（最多5条）
            var newsList = await _newsService.GetNewsByStockAsync(stockCode, cancellationToken: default);
            if (newsList.Count > 5)
            {
                newsList = newsList.Take(5).ToList();
            }

            // 构建做T分析提示词
            var newsSummary = newsList.Any() 
                ? string.Join("\n", newsList.Take(3).Select((n, i) => $"{i + 1}. {n.Title}"))
                : "暂无最新新闻";

            var analysisSummary = !string.IsNullOrWhiteSpace(analysisContent) && analysisContent.Length > 500
                ? analysisContent.Substring(0, 500) + "..."
                : analysisContent ?? "暂无详细分析";

            var tradingPrompt = $@"请为股票 {stockName}（代码：{stockCode}）生成一个简洁且数据驱动的日内做T操作方案。

【当前市场情况（多维数据）】
- 昨日收盘：{prevClosePrice:F2}元
- 今日开盘：{openPrice:F2}元
- 当前价格：{currentPrice:F2}元
- 涨跌幅：{changePercent:F2}%  | 涨跌额：{changeAmount:F2}元
- 今日最高：{highPrice:F2}元  | 今日最低：{lowPrice:F2}元
- 实时成交量：{volume:N0} 股
- 实时成交额：{turnover:N0} 元
- 换手率：{turnoverRate:F2}%
- 5日波动区间：{FormatRange(range5)}
- 10日波动区间：{FormatRange(range10)}
- 20日波动区间：{FormatRange(range20)}

【最新新闻摘要】
{newsSummary}

【AI分析摘要】
{analysisSummary}

【要求】
请充分结合上述多维度数据（价格、波动、成交量、成交额、换手率等）生成做T方案，需包含：
1. **买入价格1**：第一个建议买入价（具体价格，如69.50元）
2. **买入价格2**：第二个建议买入价（具体价格，通常比第一个更低，如69.20元）
3. **卖出价格1**：第一个建议卖出价（具体价格，如70.50元）
4. **卖出价格2**：第二个建议卖出价（具体价格，通常比第一个更高，如70.80元）
5. **操作建议**：简短说明该策略的逻辑（如量价配合、资金面、风险点），1-2句话即可

请以JSON格式返回，格式如下：
{{
  ""buyPrice1"": 69.50,
  ""buyPrice2"": 69.20,
  ""sellPrice1"": 70.50,
  ""sellPrice2"": 70.80,
  ""suggestion"": ""操作建议""
}}

只返回JSON，不要其他文字。";
_logger.LogInformation("做T方案提示词: {TradingPrompt}", tradingPrompt);
            var aiResponse = await _aiService.ExecutePromptAsync(
                promptName: null,
                userPrompt: tradingPrompt,
                placeholders: null,
                modelId: null
            );

            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                return new TradingPlanResult
                {
                    Success = false,
                    Message = "AI生成做T方案失败"
                };
            }

            // 解析AI返回的JSON
            TradingPlanData? planData = null;
            try
            {
                // 尝试提取JSON部分（如果AI返回了其他文字）
                var jsonStart = aiResponse.IndexOf('{');
                var jsonEnd = aiResponse.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonStr = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    planData = JsonConvert.DeserializeObject<TradingPlanData>(jsonStr);
                }
                else
                {
                    planData = JsonConvert.DeserializeObject<TradingPlanData>(aiResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析AI返回的做T方案JSON失败: {Response}", aiResponse);
                // 如果解析失败，尝试从文本中提取价格信息
                planData = ExtractTradingPlanFromText(aiResponse, currentPrice);
            }

            if (planData == null)
            {
                return new TradingPlanResult
                {
                    Success = false,
                    Message = "无法解析做T方案"
                };
            }

            var result = new TradingPlanResult
            {
                Success = true,
                StockCode = stockCode,
                StockName = stockName,
                BuyPrice1 = planData.BuyPrice1,
                BuyPrice2 = planData.BuyPrice2,
                SellPrice1 = planData.SellPrice1,
                SellPrice2 = planData.SellPrice2,
                Suggestion = planData.Suggestion ?? "",
                CurrentPrice = currentPrice,
                UpdateTime = DateTime.Now
            };

            _logger.LogInformation("成功生成做T方案: {StockCode}, 买入: {BuyPrice1}/{BuyPrice2}, 卖出: {SellPrice1}/{SellPrice2}", 
                stockCode, result.BuyPrice1, result.BuyPrice2, result.SellPrice1, result.SellPrice2);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成做T方案失败: {StockCode}", stockCode);
            return new TradingPlanResult
            {
                Success = false,
                Message = $"生成做T方案失败: {ex.Message}"
            };
        }
    }

    private async Task<(RangeData? FiveDay, RangeData? TenDay, RangeData? TwentyDay)> GetHistoricalRangesAsync(string stockCode)
    {
        try
        {
            var endDate = DateTime.Now.Date.AddDays(1); // 包含今日
            var startDate = endDate.AddDays(-40); // 预留足够天数，避免停盘
            var histories = await _stockDataService.GetDailyDataAsync(stockCode, startDate, endDate);

            if (histories == null || histories.Count == 0)
            {
                return (null, null, null);
            }

            var ordered = histories
                .Where(h => h != null)
                .OrderByDescending(h => h.TradeDate)
                .ToList();

            return (
                CalculateRange(ordered, 5),
                CalculateRange(ordered, 10),
                CalculateRange(ordered, 20)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取历史波动区间失败: {StockCode}", stockCode);
            return (null, null, null);
        }
    }

    private static RangeData? CalculateRange(List<StockHistory> histories, int days)
    {
        if (histories == null || histories.Count == 0)
        {
            return null;
        }

        var slice = histories
            .Where(h => h != null)
            .Take(days)
            .Where(h => h.High > 0 && h.Low > 0)
            .ToList();

        if (!slice.Any())
        {
            return null;
        }

        var high = slice.Max(h => h.High);
        var low = slice.Min(h => h.Low);
        return new RangeData(high, low);
    }

    private static string FormatRange(RangeData? range)
    {
        if (range == null)
        {
            return "暂无数据";
        }
        return $"{range.Value.High:F2}/{range.Value.Low:F2} 元";
    }

    private TradingPlanData? ExtractTradingPlanFromText(string text, decimal currentPrice)
    {
        // 尝试从文本中提取价格信息
        var buyMatches = System.Text.RegularExpressions.Regex.Matches(text, @"买入[价]?[：:1-2]?\s*([0-9.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var sellMatches = System.Text.RegularExpressions.Regex.Matches(text, @"卖出[价]?[：:1-2]?\s*([0-9.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        decimal? buyPrice1 = null, buyPrice2 = null;
        decimal? sellPrice1 = null, sellPrice2 = null;

        if (buyMatches.Count >= 1 && decimal.TryParse(buyMatches[0].Groups[1].Value, out var bp1))
            buyPrice1 = bp1;
        if (buyMatches.Count >= 2 && decimal.TryParse(buyMatches[1].Groups[1].Value, out var bp2))
            buyPrice2 = bp2;

        if (sellMatches.Count >= 1 && decimal.TryParse(sellMatches[0].Groups[1].Value, out var sp1))
            sellPrice1 = sp1;
        if (sellMatches.Count >= 2 && decimal.TryParse(sellMatches[1].Groups[1].Value, out var sp2))
            sellPrice2 = sp2;

        if (!buyPrice1.HasValue && !sellPrice1.HasValue)
        {
            return null;
        }

        return new TradingPlanData
        {
            BuyPrice1 = buyPrice1,
            BuyPrice2 = buyPrice2,
            SellPrice1 = sellPrice1,
            SellPrice2 = sellPrice2,
            Suggestion = text.Length > 200 ? text.Substring(0, 200) + "..." : text
        };
    }

    public async Task UpdateTradingPlanForStockAsync(int watchlistStockId, bool force = false)
    {
        var watchlistStock = await _context.WatchlistStocks
            .Include(w => w.Stock)
            .FirstOrDefaultAsync(w => w.Id == watchlistStockId);

        if (watchlistStock == null)
        {
            return;
        }

        // 仅在手动触发时执行 (force = true)
        if (!force)
        {
            _logger.LogDebug("跳过更新做T方案: {StockCode}, 需要手动触发", watchlistStock.StockCode);
            return;
        }

        try
        {
            var plan = await GenerateTradingPlanAsync(watchlistStock.StockCode);
            
            if (plan.Success)
            {
                var planJson = JsonConvert.SerializeObject(new
                {
                    buyPrice1 = plan.BuyPrice1,
                    buyPrice2 = plan.BuyPrice2,
                    sellPrice1 = plan.SellPrice1,
                    sellPrice2 = plan.SellPrice2,
                    suggestion = plan.Suggestion,
                    currentPrice = plan.CurrentPrice,
                    updateTime = plan.UpdateTime
                });

                watchlistStock.TradingPlan = planJson;
                watchlistStock.TradingPlanUpdateTime = plan.UpdateTime;
                watchlistStock.LastUpdate = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("成功更新做T方案: {StockCode}", watchlistStock.StockCode);
                
                // 推送更新事件到前端（若事件服务可用）
                if (_eventService != null)
                {
                    await _eventService.NotifyTradingPlanUpdatedAsync(
                        watchlistStock.Id,
                        watchlistStock.StockCode,
                        plan.UpdateTime
                    );
                }
            }
            else
            {
                _logger.LogWarning("生成做T方案失败: {StockCode}, 原因: {Message}", 
                    watchlistStock.StockCode, plan.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新做T方案失败: {StockCode}", watchlistStock.StockCode);
        }
    }
}

public readonly record struct RangeData(decimal High, decimal Low);

public class TradingPlanData
{
    public decimal? BuyPrice1 { get; set; }
    public decimal? BuyPrice2 { get; set; }
    public decimal? SellPrice1 { get; set; }
    public decimal? SellPrice2 { get; set; }
    public string? Suggestion { get; set; }
}


