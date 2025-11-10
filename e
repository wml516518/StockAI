using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Services.Interfaces;
using StockAnalyse.Api.Models;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly IStockDataService _stockDataService;
    private readonly INewsService _newsService;
    private readonly ILogger<AIController> _logger;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly IWatchlistService _watchlistService;

    private sealed class IndustryInfoResult
    {
        public string InfoText { get; set; } = string.Empty;
        public string? IndustryName { get; set; }
        public string? IndustryCode { get; set; }
        public List<string> Keywords { get; set; } = new();
    }

    public AIController(
        IAIService aiService,
        IStockDataService stockDataService,
        INewsService newsService,
        IWatchlistService watchlistService,
        ILogger<AIController> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _aiService = aiService;
        _stockDataService = stockDataService;
        _newsService = newsService;
        _watchlistService = watchlistService;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _cache = cache;
    }

    /// <summary>
    /// 批量分析股票并自动加入关注分类
    /// </summary>
    [HttpPost("analyze/batch")]
    public async Task<ActionResult<BatchAnalyzeResponse>> AnalyzeStockBatch([FromBody] BatchAnalyzeRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { message = "请求参数不能为空" });
        }

        var analysisType = string.IsNullOrWhiteSpace(request.AnalysisType)
            ? "comprehensive"
            : request.AnalysisType!.Trim().ToLowerInvariant();

        var stockCodes = new List<string>();

        if (request.StockCodes != null && request.StockCodes.Count > 0)
        {
            stockCodes.AddRange(request.StockCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => NormalizeStockCode(code)));
        }
        else if (request.WatchlistCategoryId.HasValue)
        {
            var sourceStocks = await _watchlistService.GetWatchlistByCategoryAsync(request.WatchlistCategoryId.Value);
            stockCodes.AddRange(sourceStocks
                .Where(s => !string.IsNullOrWhiteSpace(s.StockCode))
                .Select(s => NormalizeStockCode(s.StockCode)));
        }

        stockCodes = stockCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (stockCodes.Count == 0)
        {
            return BadRequest(new { message = "未提供有效的股票代码列表" });
        }

        var limit = request.Limit.HasValue && request.Limit.Value > 0
            ? Math.Min(request.Limit.Value, 50)
            : 10;

        stockCodes = stockCodes.Take(limit).ToList();

        WatchlistCategory targetCategory;
        try
        {
            targetCategory = await EnsureTargetCategoryAsync(request.TargetCategoryId, request.TargetCategoryName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "确保目标分类失败");
            return BadRequest(new { message = $"目标分类无效: {ex.Message}" });
        }

        var response = new BatchAnalyzeResponse
        {
            TargetCategoryId = targetCategory.Id,
            TargetCategoryName = targetCategory.Name
        };

        foreach (var code in stockCodes)
        {
            var item = new BatchAnalyzeItem
            {
                StockCode = code
            };

            try
            {
                var stock = await _stockDataService.GetRealTimeQuoteAsync(code);
                item.StockName = stock?.Name ?? string.Empty;

                var analysisActionResult = await AnalyzeStock(code, new AnalyzeRequest
                {
                    AnalysisType = analysisType,
                    ForceRefresh = request.ForceRefresh
                });

                var (analysisSucceeded, rating, suggestion, cached, analysisTime, errorMessage, analysisContent, technicalChartToken) =
                    ExtractAnalysisSummary(analysisActionResult);

                item.AnalysisSucceeded = analysisSucceeded;
                item.Rating = rating;
                item.ActionSuggestion = suggestion;
                item.Cached = cached;
                item.AnalysisTime = analysisTime;
                item.Analysis = analysisContent;
                item.TechnicalChart = technicalChartToken?.ToObject<object>();

                if (!analysisSucceeded)
                {
                    item.Message = errorMessage ?? "AI分析失败";
                    response.Items.Add(item);
                    continue;
                }

                try
                {
                    await _watchlistService.AddToWatchlistAsync(code, targetCategory.Id);
                    item.AddedToWatchlist = true;
                }
                catch (InvalidOperationException ex)
                {
                    item.AlreadyInWatchlist = true;
                    item.Message = ex.Message;
                    _logger.LogInformation("批量分析添加自选提示: {Message}", ex.Message);
                }
                catch (Exception ex)
                {
                    item.Message = $"添加自选股失败: {ex.Message}";
                    _logger.LogWarning(ex, "批量分析中添加自选股失败: {Code}", code);
                }
            }
            catch (Exception ex)
            {
                item.Message = $"处理失败: {ex.Message}";
                _logger.LogWarning(ex, "批量分析处理股票失败: {Code}", code);
            }

            response.Items.Add(item);
        }

        return Ok(response);
    }

    /// <summary>
    /// 分析股票（可指定提示词）
    /// </summary>
    [HttpPost("analyze/{stockCode}")]
    public async Task<ActionResult<string>> AnalyzeStock(string stockCode, [FromBody] AnalyzeRequest? request = null)
    {
        // 验证股票代码
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            _logger.LogWarning("股票代码为空");
            return BadRequest(new { message = "股票代码不能为空", error = "INVALID_STOCK_CODE" });
        }
        
        // 清理股票代码
        stockCode = stockCode.Trim().ToUpper();
        
        _logger.LogInformation("开始分析股票: {StockCode}", stockCode);
        
        // 获取分析类型（默认为comprehensive）
        var analysisType = (request?.AnalysisType ?? "comprehensive").ToLowerInvariant();
        
        // 构建缓存键（包含股票代码和分析类型）
        var cacheKey = $"ai_analysis_{stockCode}_{analysisType}";
        
        // 如果不需要强制刷新，先检查缓存
        if (!(request?.ForceRefresh ?? false))
        {
            if (_cache.TryGetValue(cacheKey, out CachedAnalysisResult? cachedResult) && cachedResult != null)
            {
                _logger.LogInformation("使用缓存的AI分析结果: {StockCode} (分析类型: {AnalysisType}, 分析时间: {AnalysisTime})", 
                    stockCode, analysisType, cachedResult.AnalysisTime);
                JToken? cachedHighlights = null;
                if (!string.IsNullOrWhiteSpace(cachedResult.TechnicalChartHighlights))
                {
                    try
                    {
                        cachedHighlights = JToken.Parse(cachedResult.TechnicalChartHighlights);
                    }
                    catch (Exception parseEx)
                    {
                        _logger.LogWarning(parseEx, "解析缓存的图表高亮信息失败");
                    }
                }

                return Ok(new
                {
                    success = true,
                    analysis = cachedResult.Analysis,
                    length = cachedResult.Analysis?.Length ?? 0,
                    timestamp = cachedResult.AnalysisTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    cached = true,
                    analysisTime = cachedResult.AnalysisTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    rating = cachedResult.Rating,
                    actionSuggestion = cachedResult.ActionSuggestion,
                    technicalChart = !string.IsNullOrWhiteSpace(cachedResult.TechnicalChartImageBase64)
                        ? new
                        {
                            imageBase64 = cachedResult.TechnicalChartImageBase64,
                            contentType = cachedResult.TechnicalChartContentType ?? "image/png",
                            highlights = cachedHighlights
                        }
                        : null
                });
            }
        }
        else
        {
            _logger.LogInformation("强制刷新，跳过缓存: {StockCode} (分析类型: {AnalysisType})", stockCode, analysisType);
        }
        
        try
        {
            string fundamentalSection = string.Empty;
            string technicalSection = string.Empty;
            string newsSection = string.Empty;
            List<string> industryKeywords = new();
            string? industryNameForNews = null;
            IndustryInfoResult? industryInfoResult = null;
            bool technicalAppendedToContext = false;
            string? technicalChartImageBase64 = null;
            string technicalChartContentType = "image/png";
            JToken? technicalChartHighlightsToken = null;
            // 获取股票基本面和实时行情数据
            // 注意：GetFundamentalInfoAsync会自动优先使用Python服务（AKShare），如果不可用则回退到其他数据源
            _logger.LogInformation("步骤1: 正在获取股票基本面信息（优先使用Python服务/AKShare数据源）...");
            
            StockFundamentalInfo? fundamentalInfo = null;
            string? dataSource = null;
            try
            {
                fundamentalInfo = await _stockDataService.GetFundamentalInfoAsync(stockCode);
                
                // 根据获取到的数据判断数据源
                // 如果Python服务成功，通常会有更完整的财务数据
                if (fundamentalInfo != null)
                {
                    // 检查是否有完整的财务数据（Python服务通常提供更多字段）
                    if (fundamentalInfo.TotalRevenue.HasValue && fundamentalInfo.NetProfit.HasValue && 
                        fundamentalInfo.ROE.HasValue && fundamentalInfo.EPS.HasValue)
                    {
                        dataSource = "Python服务 (AKShare)";
                    }
                    else if (fundamentalInfo.PE.HasValue || fundamentalInfo.PB.HasValue)
                    {
                        dataSource = "实时行情接口";
                    }
                    else
                    {
                        dataSource = "备用数据源";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取基本面信息时发生异常");
                // 继续执行，使用null值
            }
            
            if (fundamentalInfo != null)
            {
                _logger.LogInformation("成功获取基本面信息 - 数据来源: {DataSource}, 股票: {StockName}, 报告期: {ReportDate}", 
                    dataSource ?? "未知", fundamentalInfo.StockName, fundamentalInfo.ReportDate);
            }
            else
            {
                _logger.LogWarning("未能获取基本面信息，将使用实时行情数据");
            }
            
            // 步骤2: 获取行业详情
            string industryInfoText = "";
            try
            {
                _logger.LogInformation("步骤2: 正在从AKShare获取行业详情...");
                _logger.LogInformation("🤖 [AIController] 步骤2: 正在从AKShare获取行业详情");
                
                industryInfoResult = await GetIndustryInfoFromAKShareAsync(stockCode);
                industryInfoText = industryInfoResult?.InfoText ?? string.Empty;
                industryNameForNews = industryInfoResult?.IndustryName;
                if (industryInfoResult?.Keywords?.Count > 0)
                {
                    industryKeywords = industryInfoResult.Keywords;
                    if (string.IsNullOrWhiteSpace(industryNameForNews))
                    {
                        industryNameForNews = industryKeywords.FirstOrDefault();
                    }
                }
                
                if (!string.IsNullOrEmpty(industryInfoText))
                {
                    _logger.LogInformation("成功获取行业详情，数据长度: {Length} 字符", industryInfoText.Length);
                    _logger.LogInformation("🤖 [AIController] ✅ 成功获取行业详情，长度: {Length} 字符", industryInfoText.Length);
                    if (!string.IsNullOrWhiteSpace(industryNameForNews))
                    {
                        _logger.LogInformation("行业名称: {IndustryName}, 关键词: {Keywords}", industryNameForNews, string.Join("/", industryKeywords));
                    }
                }
                else
                {
                    _logger.LogWarning("未能获取行业详情");
                    _logger.LogWarning("🤖 [AIController] ⚠️ 未能获取行业详情");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取行业详情时发生异常");
                _logger.LogError(ex, "🤖 [AIController] ❌ 获取行业详情时发生异常");
                // 继续执行，使用空字符串
            }
            
            // 步骤3: 获取个股人气榜数据
            string hotRankText = "";
            try
            {
                _logger.LogInformation("步骤3: 正在从AKShare获取个股人气榜数据...");
                _logger.LogInformation("🤖 [AIController] 步骤3: 正在从AKShare获取个股人气榜数据");
                
                hotRankText = await GetHotRankFromAKShareAsync(stockCode);
                
                if (!string.IsNullOrEmpty(hotRankText))
                {
                    _logger.LogInformation("成功获取个股人气榜数据，数据长度: {Length} 字符", hotRankText.Length);
                    _logger.LogInformation("🤖 [AIController] ✅ 成功获取个股人气榜数据，长度: {Length} 字符", hotRankText.Length);
                }
                else
                {
                    _logger.LogWarning("未能获取个股人气榜数据");
                    _logger.LogWarning("🤖 [AIController] ⚠️ 未能获取个股人气榜数据");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取个股人气榜数据时发生异常");
                _logger.LogError(ex, "🤖 [AIController] ❌ 获取个股人气榜数据时发生异常");
                // 继续执行，使用空字符串
            }
            
            _logger.LogInformation("步骤2.1: 正在获取实时行情...");
            
            var stock = await _stockDataService.GetRealTimeQuoteAsync(stockCode);
            
            if (stock != null)
            {
                _logger.LogInformation("成功获取实时行情 - 股票: {StockName}, 价格: {Price}, 涨跌幅: {ChangePercent}%", 
                    stock.Name, stock.CurrentPrice, stock.ChangePercent);
            }
            else
            {
                _logger.LogWarning("未能获取实时行情");
            }
            
            // 步骤2.4: 获取近3个月的历史交易数据
            _logger.LogInformation("步骤2.4: 正在获取近3个月历史交易数据...");
            
            var endDate = DateTime.Now;
            var startDate = endDate.AddMonths(-3);
            List<StockHistory> historyData = new List<StockHistory>();
            
            try
            {
                // 计算理论交易日数量（排除周末，但保留节假日，因为节假日也可能有数据）
                // 近3个月约90天，去掉周末约26天，理论交易日约64天
                int totalDays = (int)(endDate - startDate).TotalDays;
                int weekendDays = 0;
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                        weekendDays++;
                }
                int theoreticalTradingDays = totalDays - weekendDays; // 理论上限（不考虑节假日）
                
                // 先从数据库获取历史数据
                historyData = await _stockDataService.GetDailyDataAsync(stockCode, startDate, endDate);
                
                // 如果数据不足（少于理论交易日的70%），则从API拉取
                int minExpectedDays = (int)(theoreticalTradingDays * 0.7); // 至少应该有理论交易日的70%
                if (historyData.Count < minExpectedDays)
                {
                    _logger.LogInformation("数据库历史数据不足（{Count}条，期望{Expected}条），从API拉取", historyData.Count, minExpectedDays);
                    
                    int fetchedCount = await _stockDataService.FetchAndStoreDailyHistoryAsync(stockCode, startDate, endDate);
                    _logger.LogInformation("从API拉取了 {Count} 条历史数据", fetchedCount);
                    
                    // 重新从数据库获取
                    historyData = await _stockDataService.GetDailyDataAsync(stockCode, startDate, endDate);
                }
                
                if (historyData.Count > 0)
                {
                    // 验证数据完整性
                    var sortedHistory = historyData.OrderBy(h => h.TradeDate).ToList();
                    var firstDate = sortedHistory.First().TradeDate;
                    var lastDate = sortedHistory.Last().TradeDate;
                    var actualDateRange = (lastDate - firstDate).TotalDays;
                    
                    // 检查数据连续性（检测是否有明显的日期缺失）
                    var dateSet = sortedHistory.Select(h => h.TradeDate.Date).ToHashSet();
                    int gaps = 0;
                    var missingDates = new List<DateTime>();
                    for (var date = firstDate.Date; date <= lastDate.Date; date = date.AddDays(1))
                    {
                        // 只检查工作日（周一到周五）
                        if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                        {
                            if (!dateSet.Contains(date))
                            {
                                gaps++;
                                missingDates.Add(date);
                            }
                        }
                    }
                    
                    // 数据可靠性评估
                    double completenessRatio = historyData.Count * 100.0 / theoreticalTradingDays;
                    
                    _logger.LogInformation("成功获取 {Count} 条历史交易数据（时间范围：{FirstDate} 至 {LastDate}，完整度：{Completeness:F1}%）", 
                        historyData.Count, firstDate, lastDate, completenessRatio);
                    
                    if (gaps > 0)
                    {
                        _logger.LogDebug("检测到 {Gaps} 个工作日可能缺失数据", gaps);
                    }
                }
                else
                {
                    _logger.LogWarning("未能获取历史交易数据");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取历史交易数据时发生异常");
                // 继续执行，使用空列表
            }
            
            // 步骤2.5: 获取交易数据（分时成交、买卖盘口）并缓存
            string tradeDataText = "";
            try
            {
                _logger.LogInformation("步骤2.5: 获取交易数据");
                
                // 检查缓存（缓存5分钟）
                var tradeCacheKey = $"trade_data_{stockCode}";
                if (!_cache.TryGetValue(tradeCacheKey, out string? cachedTradeData))
                {
                    var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                        ?? "http://localhost:5001";
                    
                    var tradeUrl = $"{pythonServiceUrl}/api/stock/trade/{stockCode}?data_type=all";
                    
                    using var tradeClient = new HttpClient();
                    tradeClient.Timeout = TimeSpan.FromSeconds(30);
                    tradeClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    
                    var tradeResponse = await tradeClient.GetAsync(tradeUrl);
                    
                    if (tradeResponse.IsSuccessStatusCode)
                    {
                        var tradeContent = await tradeResponse.Content.ReadAsStringAsync();
                        var tradeJson = Newtonsoft.Json.Linq.JObject.Parse(tradeContent);
                        
                        if (tradeJson["success"]?.ToString() == "True" && tradeJson["data"] != null)
                        {
                            var tradeData = tradeJson["data"] as Newtonsoft.Json.Linq.JObject;
                            
                            if (tradeData != null)
                            {
                                var dataSection = tradeData["data"] as Newtonsoft.Json.Linq.JObject;
                                
                                if (dataSection != null)
                                {
                                    // 格式化分时成交数据
                                    var minuteData = dataSection["minute"] as Newtonsoft.Json.Linq.JObject;
                                    var bidAskData = dataSection["bidAsk"] as Newtonsoft.Json.Linq.JObject;
                                    
                                    tradeDataText = "\n\n【实时交易数据】\n";
                                    
                                    // 分时成交数据
                                    if (minuteData != null && minuteData["success"]?.ToString() == "True")
                                    {
                                        var records = minuteData["records"] as Newtonsoft.Json.Linq.JArray;
                                        var count = minuteData["count"]?.ToString() ?? "0";
                                        
                                        if (records != null && records.Count > 0)
                                        {
                                            var sampleSize = Math.Min(records.Count, 200);
                                            tradeDataText += $"\n**分时成交数据**（共{count}条，显示最近{sampleSize}条）：\n";
                                            
                                            // 只显示最近 sampleSize 条
                                            var recentRecords = records.TakeLast(sampleSize).ToList();
                                            
                                            foreach (var record in recentRecords)
                                            {
                                                var rec = record as Newtonsoft.Json.Linq.JObject;
                                                if (rec != null)
                                                {
                                                    var time = rec["time"]?.ToString() ?? "";
                                                    var open = rec["open"]?.ToString() ?? "0";
                                                    var high = rec["high"]?.ToString() ?? "0";
                                                    var low = rec["low"]?.ToString() ?? "0";
                                                    var close = rec["close"]?.ToString() ?? "0";
                                                    var volume = rec["volume"]?.ToString() ?? "0";
                                                    
                                                    tradeDataText += $"- {time}: 开{open} 高{high} 低{low} 收{close} 量{volume}\n";
                                                }
                                            }
                                            
                                            // 计算分时数据统计
                                            var prices = recentRecords.Select(r => 
                                                decimal.TryParse((r as Newtonsoft.Json.Linq.JObject)?["close"]?.ToString(), out var p) ? p : 0
                                            ).Where(p => p > 0).ToList();
                                            
                                            if (prices.Count > 0)
                                            {
                                                var maxPrice = prices.Max();
                                                var minPrice = prices.Min();
                                                var avgPrice = prices.Average();
                                                var firstPrice = prices.First();
                                                var lastPrice = prices.Last();
                                                
                                                tradeDataText += $"\n*分时数据统计（最近{recentRecords.Count}条）：*\n";
                                                tradeDataText += $"- 最高价：{maxPrice:F2}元\n";
                                                tradeDataText += $"- 最低价：{minPrice:F2}元\n";
                                                tradeDataText += $"- 平均价：{avgPrice:F2}元\n";
                                                tradeDataText += $"- 价格变化：{lastPrice - firstPrice:+#.##;-#.##;0}元（{((lastPrice - firstPrice) / firstPrice * 100):+#.##;-#.##;0}%）\n";
                                            }
                                        }
                                    }
                                    
                                    // 买卖盘口数据
                                    if (bidAskData != null && bidAskData["success"]?.ToString() == "True")
                                    {
                                        var bidAskDataSection = bidAskData["data"] as Newtonsoft.Json.Linq.JObject;
                                        
                                        if (bidAskDataSection != null)
                                        {
                                            tradeDataText += $"\n**买卖盘口数据**：\n";
                                            
                                            // 解析买卖盘数据
                                            var sellData = new Dictionary<int, (decimal price, decimal volume)>();
                                            var buyData = new Dictionary<int, (decimal price, decimal volume)>();
                                            
                                            foreach (var prop in bidAskDataSection.Properties())
                                            {
                                                var key = prop.Name;
                                                var value = decimal.TryParse(prop.Value?.ToString(), out var v) ? v : 0;
                                                
                                                if (key.StartsWith("sell_") && key.EndsWith("_vol"))
                                                {
                                                    var level = int.TryParse(key.Replace("sell_", "").Replace("_vol", ""), out var l) ? l : 0;
                                                    if (level > 0 && sellData.ContainsKey(level))
                                                    {
                                                        sellData[level] = (sellData[level].price, value);
                                                    }
                                                }
                                                else if (key.StartsWith("sell_") && !key.EndsWith("_vol"))
                                                {
                                                    var level = int.TryParse(key.Replace("sell_", ""), out var l) ? l : 0;
                                                    if (level > 0)
                                                    {
                                                        if (sellData.ContainsKey(level))
                                                        {
                                                            sellData[level] = (value, sellData[level].volume);
                                                        }
                                                        else
                                                        {
                                                            sellData[level] = (value, 0);
                                                        }
                                                    }
                                                }
                                                else if (key.StartsWith("buy_") && key.EndsWith("_vol"))
                                                {
                                                    var level = int.TryParse(key.Replace("buy_", "").Replace("_vol", ""), out var l) ? l : 0;
                                                    if (level > 0 && buyData.ContainsKey(level))
                                                    {
                                                        buyData[level] = (buyData[level].price, value);
                                                    }
                                                }
                                                else if (key.StartsWith("buy_") && !key.EndsWith("_vol"))
                                                {
                                                    var level = int.TryParse(key.Replace("buy_", ""), out var l) ? l : 0;
                                                    if (level > 0)
                                                    {
                                                        if (buyData.ContainsKey(level))
                                                        {
                                                            buyData[level] = (value, buyData[level].volume);
                                                        }
                                                        else
                                                        {
                                                            buyData[level] = (value, 0);
                                                        }
                                                    }
                                                }
                                            }
                                            
                                            // 显示卖盘（从卖5到卖1）
                                            if (sellData.Count > 0)
                                            {
                                                tradeDataText += "\n*卖盘（从高到低）：*\n";
                                                foreach (var kvp in sellData.OrderByDescending(x => x.Key))
                                                {
                                   