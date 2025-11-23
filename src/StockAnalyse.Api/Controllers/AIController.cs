using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Services.Interfaces;
using StockAnalyse.Api.Models;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;

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
                    var moveResult = await _watchlistService.MoveStockToCategoryAsync(code, targetCategory.Id);

                    if (moveResult.Found)
                    {
                        if (moveResult.MovedToTarget)
                        {
                            item.AddedToWatchlist = true;
                            item.Message = "已从原分类移动到目标分类";
                        }
                        else
                        {
                            item.AlreadyInWatchlist = true;
                            item.Message = "已在目标分类，已移除其他分类";
                        }
                    }
                    else
                    {
                        await _watchlistService.AddToWatchlistAsync(code, targetCategory.Id);
                        item.AddedToWatchlist = true;
                    }
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
        
        // 获取分析类型（默认为comprehensive）
        var analysisType = (request?.AnalysisType ?? "comprehensive").ToLowerInvariant();
        
        // 构建缓存键（包含股票代码和分析类型）
        var cacheKey = $"ai_analysis_{stockCode}_{analysisType}";
        var forceRefresh = request?.ForceRefresh ?? false;
        
        // 如果不需要强制刷新，先检查缓存
        if (!forceRefresh)
        {
            if (_cache.TryGetValue(cacheKey, out CachedAnalysisResult? cachedResult) && cachedResult != null)
            {
                var cachedName = string.IsNullOrWhiteSpace(cachedResult.StockName)
                    ? stockCode
                    : cachedResult.StockName;

                _logger.LogInformation("使用缓存的AI分析结果: {StockName} (分析类型: {AnalysisType}, 分析时间: {AnalysisTime})", 
                    cachedName, analysisType, cachedResult.AnalysisTime);
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
        
        Stock? initialQuote = null;
        try
        {
            initialQuote = await _stockDataService.GetRealTimeQuoteAsync(stockCode);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "预获取股票名称失败，将使用股票代码");
        }

        var displayName = initialQuote?.Name;
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["StockCode"] = stockCode,
            ["StockName"] = displayName ?? stockCode
        });

        var stockNameForLog = displayName ?? stockCode;

        if (forceRefresh)
        {
            _logger.LogInformation("强制刷新，跳过缓存: {StockName} (分析类型: {AnalysisType})", stockNameForLog, analysisType);
        }

        _logger.LogInformation("开始分析股票: {StockName}", stockNameForLog);

        try
        {
            return await ExecuteStockAnalysisCore(stockCode, stockNameForLog, initialQuote, request, analysisType, cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分析股票 {StockName} 失败，尝试使用原始上下文进行降级分析", stockNameForLog);
            
            try
            {
                var result = await _aiService.AnalyzeStockAsync(stockCode, request?.PromptId, request?.Context, request?.ModelId);
                
                // 确保返回正确的响应格式
                if (string.IsNullOrEmpty(result))
                {
                    _logger.LogWarning("🤖 [AIController] ⚠️ 降级分析结果为空");
                    return Ok(new { 
                        success = false, 
                        analysis = "AI分析失败，请检查AI服务配置。",
                        error = ex.Message
                    });
                }
                
                // 保存到缓存（永久缓存）
                var analysisTime = DateTime.Now;
                var cachedResult = new CachedAnalysisResult
                {
                    Analysis = result,
                    AnalysisTime = analysisTime,
                    StockCode = stockCode,
                    StockName = stockNameForLog,
                    AnalysisType = analysisType,
                    TechnicalChartImageBase64 = null,
                    TechnicalChartContentType = "image/png",
                    TechnicalChartHighlights = null,
                    Rating = null,
                    ActionSuggestion = null
                };
                
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    Priority = CacheItemPriority.NeverRemove,
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(2)
                };
                _cache.Set(cacheKey, cachedResult, cacheOptions);
                
                _logger.LogInformation("降级分析结果已缓存: {StockName} (分析类型: {AnalysisType})", stockNameForLog, analysisType);
                
                return Ok(new { 
                    success = true, 
                    analysis = result,
                    length = result.Length,
                    timestamp = analysisTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    cached = false,
                    analysisTime = analysisTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    rating = (string?)null,
                    actionSuggestion = (string?)null,
                    technicalChart = (object?)null
                });
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "🤖 [AIController] ❌ {StockName} 降级分析也失败", stockNameForLog);
                return Ok(new { 
                    success = false, 
                    analysis = $"AI分析失败: {ex.Message}",
                    error = ex2.Message
                });
            }
        }
    }

    private async Task<ActionResult<string>> ExecuteStockAnalysisCore(
        string stockCode,
        string stockNameForLog,
        Stock? initialQuote,
        AnalyzeRequest? request,
        string analysisType,
        string cacheKey)
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
                fundamentalInfo = await _stockDataService.GetFundamentalInfoAsync(stockCode, request?.ForceRefresh ?? false);
                
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
            
        var stock = initialQuote ?? await _stockDataService.GetRealTimeQuoteAsync(stockCode);
        var effectiveStockName = stock?.Name ?? stockNameForLog;
        stockNameForLog = effectiveStockName;
            
            if (stock != null)
            {
                _logger.LogInformation("成功获取实时行情 - 股票: {StockName}, 价格: {Price}, 涨跌幅: {ChangePercent}%", 
                effectiveStockName, stock.CurrentPrice, stock.ChangePercent);
            }
            else
            {
            _logger.LogWarning("未能获取实时行情 - 股票: {StockName}", stockNameForLog);
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
                                                    tradeDataText += $"- 卖{kvp.Key}：{kvp.Value.price:F2}元 量{kvp.Value.volume:F0}手\n";
                                                }
                                            }
                                            
                                            // 显示买盘（从买1到买5）
                                            if (buyData.Count > 0)
                                            {
                                                tradeDataText += "\n*买盘（从高到低）：*\n";
                                                foreach (var kvp in buyData.OrderByDescending(x => x.Key))
                                                {
                                                    tradeDataText += $"- 买{kvp.Key}：{kvp.Value.price:F2}元 量{kvp.Value.volume:F0}手\n";
                                                }
                                            }
                                            
                                            // 分析买卖盘口
                                            if (sellData.Count > 0 && buyData.Count > 0)
                                            {
                                                var sell1Price = sellData.ContainsKey(1) ? sellData[1].price : 0;
                                                var buy1Price = buyData.ContainsKey(1) ? buyData[1].price : 0;
                                                
                                                if (sell1Price > 0 && buy1Price > 0)
                                                {
                                                    var spread = sell1Price - buy1Price;
                                                    var spreadPercent = (spread / buy1Price) * 100;
                                                    
                                                    tradeDataText += $"\n*盘口分析：*\n";
                                                    tradeDataText += $"- 卖一价：{sell1Price:F2}元\n";
                                                    tradeDataText += $"- 买一价：{buy1Price:F2}元\n";
                                                    tradeDataText += $"- 价差：{spread:F2}元（{spreadPercent:F2}%）\n";
                                                    
                                                    var totalSellVolume = sellData.Values.Sum(v => v.volume);
                                                    var totalBuyVolume = buyData.Values.Sum(v => v.volume);
                                                    
                                                    tradeDataText += $"- 卖盘总量：{totalSellVolume:F0}手\n";
                                                    tradeDataText += $"- 买盘总量：{totalBuyVolume:F0}手\n";
                                                    tradeDataText += $"- 买卖比：{(totalBuyVolume > 0 ? (totalSellVolume / totalBuyVolume).ToString("F2") : "N/A")}\n";
                                                }
                                            }
                                        }
                                    }
                                    
                                    tradeDataText += "\n**提示：请结合以上实时交易数据（分时成交、买卖盘口），分析当前市场情绪和交易活跃度，判断买卖力量的对比。**\n";
                                    
                                    // 缓存5分钟
                                    _cache.Set(tradeCacheKey, tradeDataText, TimeSpan.FromMinutes(5));
                                    
                                    _logger.LogDebug("交易数据获取完成，数据长度: {Length} 字符", tradeDataText.Length);
                                    _logger.LogInformation("🤖 [AIController] ✅ 交易数据获取完成，已缓存");
                                }
                            }
                        }
                    }
                }
                else
                {
                    tradeDataText = cachedTradeData ?? "";
                    _logger.LogDebug("使用缓存的交易数据");
                    _logger.LogInformation("🤖 [AIController] ✅ 使用缓存的交易数据");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取交易数据时发生异常");
                _logger.LogWarning(ex, "🤖 [AIController] ⚠️ 获取交易数据时发生异常");
                // 继续执行，不影响其他分析
            }
            
            // 步骤2.6: 调用Python服务进行大数据分析（AKShare数据源）
            string pythonAnalysisText = "";
            try
            {
                _logger.LogInformation("步骤2.6: 调用Python服务进行大数据分析");
                _logger.LogInformation("🤖 [AIController] 步骤2.6: 调用Python服务进行大数据分析");
                
                var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                    ?? "http://localhost:5001";
                
                var analyzeUrl = $"{pythonServiceUrl}/api/stock/analyze/{stockCode}?months=3";
                
                // 创建独立的HttpClient，设置更长的超时时间（Python分析需要获取数据并计算指标，可能需要较长时间）
                using var pythonClient = new HttpClient();
                pythonClient.Timeout = TimeSpan.FromSeconds(180); // 增加到180秒（3分钟），因为需要获取历史数据+分析
                pythonClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                
                _logger.LogDebug("正在调用Python分析服务（超时时间：180秒）");
                var analyzeResponse = await pythonClient.GetAsync(analyzeUrl);
                
                if (analyzeResponse.IsSuccessStatusCode)
                {
                    var analyzeContent = await analyzeResponse.Content.ReadAsStringAsync();
                    var analyzeJson = Newtonsoft.Json.Linq.JObject.Parse(analyzeContent);
                    
                    if (analyzeJson["success"]?.ToString() == "True" && analyzeJson["data"] != null)
                    {
                        var analysisData = analyzeJson["data"] as Newtonsoft.Json.Linq.JObject;
                        
                        if (analysisData != null)
                        {
                            // 格式化Python分析结果
                            var indicators = analysisData["indicators"] as Newtonsoft.Json.Linq.JObject;
                            var trends = analysisData["trends"] as Newtonsoft.Json.Linq.JObject;
                            var statistics = analysisData["statistics"] as Newtonsoft.Json.Linq.JObject;
                            var insights = analysisData["insights"] as Newtonsoft.Json.Linq.JArray;
                            var chart = analysisData["chart"] as Newtonsoft.Json.Linq.JObject;
                            
                            if (chart != null)
                            {
                                technicalChartImageBase64 = chart["imageBase64"]?.ToString();
                                var contentTypeFromChart = chart["contentType"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(contentTypeFromChart))
                                {
                                    technicalChartContentType = contentTypeFromChart;
                                }
                                technicalChartHighlightsToken = chart["highlights"];
                            }
                            
                            // 辅助函数：安全获取数值
                            Func<Newtonsoft.Json.Linq.JToken?, string> SafeGetDouble = (token) => 
                                token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.Float || token.Type == Newtonsoft.Json.Linq.JTokenType.Integer 
                                    ? ((double)token).ToString("F2") : "N/A";
                            
                            Func<Newtonsoft.Json.Linq.JToken?, string> SafeGetDouble4 = (token) => 
                                token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.Float || token.Type == Newtonsoft.Json.Linq.JTokenType.Integer 
                                    ? ((double)token).ToString("F4") : "N/A";
                            
                            Func<Newtonsoft.Json.Linq.JToken?, string> SafeGetString = (token) => 
                                token != null ? token.ToString() : "N/A";
                            
                            var ma = indicators?["MA"] as Newtonsoft.Json.Linq.JObject;
                            var macd = indicators?["MACD"] as Newtonsoft.Json.Linq.JObject;
                            var rsi = indicators?["RSI"] as Newtonsoft.Json.Linq.JObject;
                            var bb = indicators?["BollingerBands"] as Newtonsoft.Json.Linq.JObject;
                            
                            pythonAnalysisText = $@"

【Python大数据分析结果】（基于AKShare数据源，分析期：{SafeGetString(analysisData["period"])}，数据条数：{SafeGetString(analysisData["totalRecords"])}条）

**基础统计信息：**
- 期初价格：{SafeGetDouble(statistics?["startPrice"])}元
- 期末价格：{SafeGetDouble(statistics?["endPrice"])}元
- 最高价：{SafeGetDouble(statistics?["highestPrice"])}元
- 最低价：{SafeGetDouble(statistics?["lowestPrice"])}元
- 平均价格：{SafeGetDouble(statistics?["averagePrice"])}元
- 价格涨跌幅：{SafeGetDouble(statistics?["priceChange"])}元（{SafeGetDouble(statistics?["priceChangePercent"])}%）
- 波动率：{SafeGetDouble(statistics?["volatility"])}%

**技术指标分析：**

*移动平均线(MA)：*
- MA5：{SafeGetDouble(ma?["MA5"])}元
- MA10：{SafeGetDouble(ma?["MA10"])}元
- MA20：{SafeGetDouble(ma?["MA20"])}元
- MA60：{SafeGetDouble(ma?["MA60"])}元
- 趋势：{(SafeGetString(ma?["trend"]) == "up" ? "上升趋势" : "下降趋势")}

*MACD指标：*
- MACD值：{SafeGetDouble4(macd?["MACD"])}
- Signal信号线：{SafeGetDouble4(macd?["Signal"])}
- Histogram柱状图：{SafeGetDouble4(macd?["Histogram"])}
- 信号：{(SafeGetString(macd?["signal"]) == "bullish" ? "看涨信号" : "看跌信号")}

*RSI相对强弱指标：*
- RSI值：{SafeGetDouble(rsi?["RSI"])}
- 信号：{(SafeGetString(rsi?["signal"]) == "overbought" ? "超买（>70）" : SafeGetString(rsi?["signal"]) == "oversold" ? "超卖（<30）" : "中性（30-70）")}

*布林带(Bollinger Bands)：*
- 上轨：{SafeGetDouble(bb?["Upper"])}元
- 中轨：{SafeGetDouble(bb?["Middle"])}元
- 下轨：{SafeGetDouble(bb?["Lower"])}元
- 价格位置：{(SafeGetString(bb?["position"]) == "above" ? "上轨上方（超买）" : SafeGetString(bb?["position"]) == "below" ? "下轨下方（超卖）" : "中轨附近（正常）")}

**趋势分析：**
- 价格趋势：{(SafeGetString(trends?["priceTrend"]) == "up" ? "上升" : "下降")}
- 成交量趋势：{(SafeGetString(trends?["volumeTrend"]) == "increase" ? "放大" : "萎缩")}
- 动量：{(SafeGetString(trends?["momentum"]) == "strong" ? "强劲" : "温和")}
- 波动率趋势：{(SafeGetString(trends?["volatilityTrend"]) == "high" ? "高波动" : "低波动")}

**关键洞察：**
";
                            
                            if (insights != null && insights.Count > 0)
                            {
                                foreach (var insight in insights)
                                {
                                    pythonAnalysisText += $"- {insight}\n";
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(technicalChartImageBase64))
                            {
                                pythonAnalysisText += $@"

**图表洞察：**
- 已生成股价走势图，标注最高价、最低价及当前价等关键点位，请在页面中查看图表并结合文字分析综合判断。
";
                            }

                            pythonAnalysisText += $@"

**提示：请结合以上Python大数据分析结果（技术指标、趋势分析等），结合基本面信息和历史交易数据，给出综合的投资建议和未来走势预测。**
";
                            
                            _logger.LogInformation("Python大数据分析完成，分析结果长度: {Length} 字符", pythonAnalysisText.Length);
                            _logger.LogInformation("🤖 [AIController] ✅ Python大数据分析完成，结果长度: {Length} 字符", pythonAnalysisText.Length);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Python分析服务返回失败: {Error}", analyzeJson["error"]?.ToString() ?? "未知错误");
                        _logger.LogWarning("🤖 [AIController] ⚠️ Python分析服务返回失败");
                    }
                }
                else
                {
                    _logger.LogWarning("Python分析服务不可用（状态码: {StatusCode}），将使用基础分析", (int)analyzeResponse.StatusCode);
                    _logger.LogWarning("🤖 [AIController] ⚠️ Python分析服务不可用（状态码: {StatusCode}）", (int)analyzeResponse.StatusCode);
                }
            }
            catch (System.Threading.Tasks.TaskCanceledException ex) when (ex.InnerException is System.TimeoutException || ex.Message.Contains("Timeout"))
            {
                _logger.LogWarning("Python分析服务请求超时（已设置180秒超时），将使用基础历史数据分析");
                _logger.LogWarning(ex, "🤖 [AIController] ⚠️ Python分析服务请求超时");
                // 继续执行，使用基础分析
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "调用Python分析服务时发生异常，将使用基础历史数据分析");
                _logger.LogWarning(ex, "🤖 [AIController] ⚠️ 调用Python分析服务时发生异常");
                // 继续执行，使用基础分析
            }
            
            // 构建包含基本面信息的上下文
            string? enhancedContext = request?.Context;
            
            // 格式化历史交易数据
            string historyText = "";
            if (historyData != null && historyData.Count > 0)
            {
                _logger.LogDebug("步骤3: 格式化历史交易数据");
                _logger.LogInformation("🤖 [AIController] 步骤3: 格式化历史交易数据");
                
                // 按日期排序（从旧到新）
                var sortedHistory = historyData.OrderBy(h => h.TradeDate).ToList();
                
                if (sortedHistory.Count > 0)
                {
                    // 计算关键统计指标
                    var closes = sortedHistory.Select(h => h.Close).ToList();
                    var volumes = sortedHistory.Select(h => h.Volume).ToList();
                    var turnovers = sortedHistory.Select(h => h.Turnover).ToList();
                    
                    decimal maxPrice = closes.Max();
                    decimal minPrice = closes.Min();
                    decimal avgPrice = closes.Average();
                    decimal currentClose = closes.Last();
                    decimal firstClose = closes.First();
                    decimal priceChange = currentClose - firstClose;
                    decimal priceChangePercent = firstClose != 0 ? (priceChange / firstClose) * 100 : 0;
                    
                    decimal avgVolume = volumes.Average();
                    decimal maxVolume = volumes.Max();
                    decimal minVolume = volumes.Min();
                    decimal avgTurnover = turnovers.Average();
                    
                    // 计算价格趋势（最近10个交易日 vs 前10个交易日）
                    int recentDays = Math.Min(10, sortedHistory.Count);
                    int earlyDays = Math.Min(10, sortedHistory.Count);
                    decimal recentAvgPrice = sortedHistory.TakeLast(recentDays).Average(h => h.Close);
                    decimal earlyAvgPrice = sortedHistory.Take(earlyDays).Average(h => h.Close);
                    decimal trendPercent = earlyAvgPrice != 0 ? ((recentAvgPrice - earlyAvgPrice) / earlyAvgPrice) * 100 : 0;
                    
                    // 计算成交量趋势
                    decimal recentAvgVolume = sortedHistory.TakeLast(recentDays).Average(h => h.Volume);
                    decimal earlyAvgVolume = sortedHistory.Take(earlyDays).Average(h => h.Volume);
                    decimal volumeTrendPercent = earlyAvgVolume != 0 ? ((recentAvgVolume - earlyAvgVolume) / earlyAvgVolume) * 100 : 0;
                
                // 构建历史数据文本
                historyText = $@"

【近3个月交易数据统计】（共{historyData.Count}个交易日，从{startDate:yyyy-MM-dd}至{endDate:yyyy-MM-dd}）

**价格走势：**
- 期初价格：{firstClose:F2}元
- 期末价格：{currentClose:F2}元
- 期间涨跌：{priceChange:+#.##;-#.##;0}元（{priceChangePercent:+#.##;-#.##;0}%）
- 最高价：{maxPrice:F2}元
- 最低价：{minPrice:F2}元
- 平均价格：{avgPrice:F2}元

**价格趋势分析：**
- 最近{recentDays}个交易日平均价：{recentAvgPrice:F2}元
- 前{earlyDays}个交易日平均价：{earlyAvgPrice:F2}元
- 价格趋势：{(trendPercent > 0 ? "上涨" : trendPercent < 0 ? "下跌" : "持平")} {Math.Abs(trendPercent):F2}%

**成交量分析：**
- 平均成交量：{avgVolume:F0}手
- 最大成交量：{maxVolume:F0}手
- 最小成交量：{minVolume:F0}手
- 平均成交额：{avgTurnover:F2}万元
- 成交量趋势：{(volumeTrendPercent > 0 ? "放大" : volumeTrendPercent < 0 ? "萎缩" : "持平")} {Math.Abs(volumeTrendPercent):F2}%

**近期关键交易日数据（最近10个交易日）：**
";
                
                // 添加最近10个交易日的详细数据
                var recentHistory = sortedHistory.TakeLast(10).ToList();
                for (int i = 0; i < recentHistory.Count; i++)
                {
                    var day = recentHistory[i];
                    // 找到该日在完整列表中的索引
                    int dayIndex = sortedHistory.FindIndex(h => h.TradeDate == day.TradeDate);
                    // 获取前一个交易日的收盘价
                    decimal prevClose = dayIndex > 0 ? sortedHistory[dayIndex - 1].Close : day.Open;
                    decimal dayChange = day.Close - prevClose;
                    decimal dayChangePercent = prevClose != 0 ? (dayChange / prevClose) * 100 : 0;
                    
                    historyText += $"- {day.TradeDate:yyyy-MM-dd}: 开盘{day.Open:F2}元, 收盘{day.Close:F2}元, 最高{day.High:F2}元, 最低{day.Low:F2}元, 涨跌{dayChange:+#.##;-#.##;0}元({dayChangePercent:+#.##;-#.##;0}%), 成交量{day.Volume:F0}手, 成交额{day.Turnover:F2}万元\n";
                }
                
                historyText += $@"

**提示：请根据以上历史交易数据，结合当前价格和基本面信息，分析该股票的价格走势，并给出未来可能的走势预测。重点关注：**
1. 价格趋势是否与成交量变化一致
2. 最近的价格波动是否有异常
3. 结合基本面数据，判断当前价格是否合理
4. 基于历史走势，预测未来1-2周可能的股价走势
";
                }
                
                _logger.LogDebug("已格式化历史交易数据，数据长度: {Length} 字符", historyText.Length);
                _logger.LogInformation("🤖 [AIController] ✅ 已格式化历史交易数据，长度: {Length} 字符", historyText.Length);
            }
            
            technicalSection = $"{historyText}{pythonAnalysisText}{tradeDataText}".Trim();
            
            if (fundamentalInfo != null)
            {
                _logger.LogDebug("步骤4: 构建包含基本面信息的分析上下文");
                _logger.LogInformation("🤖 [AIController] 步骤4: 构建包含基本面信息的分析上下文");
                
                var dataSourceNote = !string.IsNullOrEmpty(dataSource) ? $"（数据来源：{dataSource}）" : "";
                var fundamentalText = $@"

【最新财务数据】{dataSourceNote}（报告期：{fundamentalInfo.ReportDate ?? "未知"}，报告类型：{fundamentalInfo.ReportType ?? "未知"}）

**主要财务指标：**
- 营业收入：{(fundamentalInfo.TotalRevenue.HasValue ? fundamentalInfo.TotalRevenue.Value.ToString("F2") + "万元" : "N/A")}
- 净利润：{(fundamentalInfo.NetProfit.HasValue ? fundamentalInfo.NetProfit.Value.ToString("F2") + "万元" : "N/A")}
- 每股收益(EPS)：{(fundamentalInfo.EPS.HasValue ? fundamentalInfo.EPS.Value.ToString("F3") + "元" : "N/A")}
- 每股净资产(BPS)：{(fundamentalInfo.BPS.HasValue ? fundamentalInfo.BPS.Value.ToString("F3") + "元" : "N/A")}

**盈利能力：**
- 净资产收益率(ROE)：{(fundamentalInfo.ROE.HasValue ? fundamentalInfo.ROE.Value.ToString("F2") + "%" : "N/A")}
- 毛利率：{(fundamentalInfo.GrossProfitMargin.HasValue ? fundamentalInfo.GrossProfitMargin.Value.ToString("F2") + "%" : "N/A")}
- 净利率：{(fundamentalInfo.NetProfitMargin.HasValue ? fundamentalInfo.NetProfitMargin.Value.ToString("F2") + "%" : "N/A")}

**成长性：**
- 营收增长率：{(fundamentalInfo.RevenueGrowthRate.HasValue ? fundamentalInfo.RevenueGrowthRate.Value.ToString("F2") + "%" : "N/A")}
- 净利润增长率：{(fundamentalInfo.ProfitGrowthRate.HasValue ? fundamentalInfo.ProfitGrowthRate.Value.ToString("F2") + "%" : "N/A")}

**偿债能力：**
- 资产负债率：{(fundamentalInfo.AssetLiabilityRatio.HasValue ? fundamentalInfo.AssetLiabilityRatio.Value.ToString("F2") + "%" : "N/A")}
- 流动比率：{(fundamentalInfo.CurrentRatio.HasValue ? fundamentalInfo.CurrentRatio.Value.ToString("F2") : "N/A")}
- 速动比率：{(fundamentalInfo.QuickRatio.HasValue ? fundamentalInfo.QuickRatio.Value.ToString("F2") : "N/A")}

**运营能力：**
- 存货周转率：{(fundamentalInfo.InventoryTurnover.HasValue ? fundamentalInfo.InventoryTurnover.Value.ToString("F2") : "N/A")}
- 应收账款周转率：{(fundamentalInfo.AccountsReceivableTurnover.HasValue ? fundamentalInfo.AccountsReceivableTurnover.Value.ToString("F2") : "N/A")}

**估值指标：**
- 市盈率(PE)：{(fundamentalInfo.PE.HasValue ? fundamentalInfo.PE.Value.ToString("F2") : stock?.PE?.ToString("F2") ?? "N/A")}
- 市净率(PB)：{(fundamentalInfo.PB.HasValue ? fundamentalInfo.PB.Value.ToString("F2") : stock?.PB?.ToString("F2") ?? "N/A")}
";
                
                fundamentalSection = (fundamentalText + industryInfoText + hotRankText).Trim();
                if (!string.IsNullOrEmpty(fundamentalSection))
                {
                    enhancedContext = string.IsNullOrEmpty(enhancedContext)
                        ? fundamentalSection
                        : $"{enhancedContext}{fundamentalSection}";
                }
                
                _logger.LogDebug("已构建包含基本面信息和历史数据的上下文，上下文长度: {Length} 字符", enhancedContext?.Length ?? 0);
                _logger.LogInformation("🤖 [AIController] ✅ 已构建包含基本面信息和历史数据的上下文，长度: {Length} 字符", enhancedContext?.Length ?? 0);
            }
            else if (stock != null)
            {
                _logger.LogDebug("使用实时行情数据构建分析上下文（未获取到基本面数据）");
                _logger.LogInformation("🤖 [AIController] ⚠️ 使用实时行情数据构建分析上下文（未获取到基本面数据）");
                
                // 如果没有基本面数据，至少提供实时行情数据
                var stockInfo = $@"

**当前行情数据：**
- 当前价格：{stock.CurrentPrice:F2}元
- 涨跌幅：{stock.ChangePercent:F2}%
- 市盈率(PE)：{(stock.PE?.ToString("F2") ?? "N/A")}
- 市净率(PB)：{(stock.PB?.ToString("F2") ?? "N/A")}
- 换手率：{stock.TurnoverRate:F2}%
";
                fundamentalSection = (stockInfo + industryInfoText + hotRankText).Trim();
                if (!string.IsNullOrEmpty(fundamentalSection))
                {
                    enhancedContext = string.IsNullOrEmpty(enhancedContext)
                        ? fundamentalSection
                        : $"{enhancedContext}{fundamentalSection}";
                }
            }
            else
            {
                _logger.LogWarning("既未获取到基本面数据，也未获取到实时行情数据，将使用原始上下文");
                _logger.LogWarning("🤖 [AIController] ⚠️ 既未获取到基本面数据，也未获取到实时行情数据，将使用原始上下文");
                
                // 即使没有基本面和实时行情，也尝试添加历史数据
                if (!string.IsNullOrEmpty(historyText) || !string.IsNullOrEmpty(pythonAnalysisText) || !string.IsNullOrEmpty(tradeDataText) || 
                    !string.IsNullOrEmpty(industryInfoText) || !string.IsNullOrEmpty(hotRankText))
                {
                    fundamentalSection = (industryInfoText + hotRankText).Trim();
                    var combinedBuilder = new StringBuilder();
                    if (!string.IsNullOrEmpty(fundamentalSection))
                    {
                        combinedBuilder.AppendLine(fundamentalSection);
                    }
                    if (!string.IsNullOrEmpty(technicalSection))
                    {
                        combinedBuilder.AppendLine(technicalSection);
                        technicalAppendedToContext = true;
                    }
                    var combinedContext = combinedBuilder.ToString();
                    if (!string.IsNullOrEmpty(combinedContext))
                    {
                        enhancedContext = string.IsNullOrEmpty(enhancedContext) 
                            ? combinedContext
                            : $"{enhancedContext}{combinedContext}";
                    }
                }
            }
            
            if (!technicalAppendedToContext && !string.IsNullOrEmpty(technicalSection))
            {
                enhancedContext = string.IsNullOrEmpty(enhancedContext)
                    ? technicalSection
                    : $"{enhancedContext}{technicalSection}";
                technicalAppendedToContext = true;
            }
            
            // 获取新闻舆论信息
            try
            {
                var stockNewsList = await _newsService.GetNewsByStockAsync(stockCode, request?.ForceRefresh ?? false) ?? new List<FinancialNews>();                                             
            _logger.LogInformation("获取到与股票 {StockName} 直接相关的新闻 {Count} 条", stockNameForLog, stockNewsList.Count);                                

                if (stockNewsList.Count > 0)
                {
                    var builder = new StringBuilder();
                    builder.AppendLine("【新闻舆论信息】");

                    builder.AppendLine("与股票直接相关的新闻：");
                    AppendNewsItems(builder, stockNewsList.OrderByDescending(n => n.PublishTime).Take(6));

                    builder.AppendLine("\n请结合上述新闻，分析市场情绪、重大事件及潜在影响。");                                                                
                    newsSection = builder.ToString().Trim();
                }
                else
                {
                    newsSection = "【新闻舆论信息】\n当前未获取到与该股票相关的新闻，请提示用户关注公司公告、政策变化及行业消息。";
                }

                if (!string.IsNullOrEmpty(newsSection))
                {
                    enhancedContext = string.IsNullOrEmpty(enhancedContext)    
                        ? newsSection
                        : $"{enhancedContext}{newsSection}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取新闻舆论信息时发生异常");
                newsSection = "【新闻舆论信息】\n新闻数据获取失败，请提醒用户稍后重试或手动关注相关新闻。";
                enhancedContext = string.IsNullOrEmpty(enhancedContext)
                    ? newsSection
                    : $"{enhancedContext}{newsSection}";
            }
            
            _logger.LogInformation("步骤5: 调用AI服务进行分析");
            _logger.LogInformation("🤖 [AIController] 步骤5: 调用AI服务进行分析");
            
            var placeholders = new Dictionary<string, string?>
            {
                ["{stockCode}"] = stockCode
            };

            string finalResult;
            string fundamentalAnalysisResult = string.Empty;
            string newsAnalysisResult = string.Empty;
            string technicalAnalysisResult = string.Empty;

            switch (analysisType)
            {
                case "fundamental":
                {
                    var contextForFundamental = !string.IsNullOrWhiteSpace(fundamentalSection)
                        ? fundamentalSection
                        : "当前未能获取到详尽的基本面数据，请结合常见财务指标与行业逻辑给出分析。";
                    finalResult = await _aiService.ExecutePromptAsync(
                        "基本面分析",
                        BuildFundamentalPrompt(contextForFundamental),
                        placeholders,
                        request?.ModelId
                    );
                    break;
                }
                case "news":
                {
                    var contextForNews = !string.IsNullOrWhiteSpace(newsSection)
                        ? newsSection
                        : "当前未获取到相关新闻，请提示需重点关注公司公告、政策变化及行业消息。";
                    finalResult = await _aiService.ExecutePromptAsync(
                        "新闻分析",
                        BuildNewsPrompt(contextForNews),
                        placeholders,
                        request?.ModelId
                    );
                    break;
                }
                case "technical":
                {
                    var contextForTechnical = !string.IsNullOrWhiteSpace(technicalSection)
                        ? technicalSection
                        : "当前未能获取到详细的技术面数据，请根据有限信息给出趋势判断。";
                    finalResult = await _aiService.ExecutePromptAsync(
                        "技术分析",
                        BuildTechnicalPrompt(contextForTechnical),
                        placeholders,
                        request?.ModelId
                    );
                    break;
                }
                default:
                {
                    var contextForFundamental = !string.IsNullOrWhiteSpace(fundamentalSection)
                        ? fundamentalSection
                        : "当前未能获取到详尽的基本面数据，请结合常见财务指标与行业逻辑进行分析。";
                    fundamentalAnalysisResult = await _aiService.ExecutePromptAsync(
                        "基本面分析",
                        BuildFundamentalPrompt(contextForFundamental),
                        placeholders,
                        request?.ModelId
                    );

                    var contextForNews = !string.IsNullOrWhiteSpace(newsSection)
                        ? newsSection
                        : "当前未获取到相关新闻，请提示需重点关注公司公告、政策变化及行业消息。";
                    newsAnalysisResult = await _aiService.ExecutePromptAsync(
                        "新闻分析",
                        BuildNewsPrompt(contextForNews),
                        placeholders,
                        request?.ModelId
                    );

                    var contextForTechnical = !string.IsNullOrWhiteSpace(technicalSection)
                        ? technicalSection
                        : "当前未能获取到详细的技术面数据，请根据有限信息给出趋势判断。";
                    technicalAnalysisResult = await _aiService.ExecutePromptAsync(
                        "技术分析",
                        BuildTechnicalPrompt(contextForTechnical),
                        placeholders,
                        request?.ModelId
                    );

                    var combinedResultsBuilder = new StringBuilder();
                    combinedResultsBuilder.AppendLine("【基本面分析】");
                    combinedResultsBuilder.AppendLine(!string.IsNullOrWhiteSpace(fundamentalAnalysisResult) ? fundamentalAnalysisResult : "未获取到基本面分析结果。");
                    combinedResultsBuilder.AppendLine();
                    combinedResultsBuilder.AppendLine("【新闻面分析】");
                    combinedResultsBuilder.AppendLine(!string.IsNullOrWhiteSpace(newsAnalysisResult) ? newsAnalysisResult : "未获取到新闻面分析结果。");
                    combinedResultsBuilder.AppendLine();
                    combinedResultsBuilder.AppendLine("【技术面分析】");
                    combinedResultsBuilder.AppendLine(!string.IsNullOrWhiteSpace(technicalAnalysisResult) ? technicalAnalysisResult : "未获取到技术面分析结果。");

                    var combinedResults = combinedResultsBuilder.ToString().Trim();
                    var summaryPlaceholders = new Dictionary<string, string?>
                    {
                        ["{stockCode}"] = stockCode,
                        ["{stockResult}"] = combinedResults
                    };

                    var summaryPrompt = @"以下是股票{stockCode}的多维度分析结果：
{stockResult}

请扮演资深投资顾问，从以下角度输出综合总结：
1. 总体判断
2. 主要机会
3. 主要风险
4. 操作建议
5. 需持续关注的要点";

                    var resolvedSummaryPrompt = ApplyPlaceholders(summaryPrompt, summaryPlaceholders);

                    finalResult = await _aiService.ExecutePromptAsync(
                        "综合分析",
                        summaryPrompt,
                        summaryPlaceholders,
                        request?.ModelId
                    );
                    break;
                }
            }

            _logger.LogInformation("AI分析完成，结果长度: {Length} 字符", finalResult?.Length ?? 0);
            _logger.LogInformation("🤖 [AIController] ✅ AI分析完成，结果长度: {Length} 字符", finalResult?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(finalResult))
            {
                _logger.LogWarning("🤖 [AIController] ⚠️ AI分析结果为空");
                return Ok("AI分析完成，但未返回结果。请检查AI服务配置。");
            }

            var responseSizeKB = (finalResult.Length * 2) / 1024.0;
        _logger.LogDebug("[{StockName}] 响应大小估算: {SizeKB:F2} KB", stockNameForLog, responseSizeKB);
        _logger.LogInformation("🤖 [AIController] 📊 [{StockName}] 响应大小估算: {SizeKB:F2} KB", stockNameForLog, responseSizeKB);
            
            // 如果响应太大，给出警告
            if (responseSizeKB > 500)
            {
            _logger.LogWarning("🤖 [AIController] ⚠️ [{StockName}] 响应较大 ({SizeKB:F2} KB)，可能影响传输", stockNameForLog, responseSizeKB);
            }

            string? rating = null;
            string? actionSuggestion = null;
            try
            {
                var recommendationPrompt = BuildRecommendationSummaryPrompt(stockCode, finalResult);
                var recommendationResponse = await _aiService.ExecutePromptAsync(
                    null,
                    recommendationPrompt,
                    placeholders,
                    request?.ModelId
                );

                if (!string.IsNullOrWhiteSpace(recommendationResponse))
                {
                    var trimmed = recommendationResponse.Trim();
                    if (trimmed.StartsWith("```", StringComparison.Ordinal))
                    {
                        var firstBreak = trimmed.IndexOf('\n');
                        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                        if (firstBreak >= 0 && lastFence > firstBreak)
                        {
                            trimmed = trimmed.Substring(firstBreak + 1, lastFence - firstBreak - 1).Trim();
                        }
                    }

                    var summaryJson = JObject.Parse(trimmed);
                    rating = summaryJson["rating"]?.ToString()?.Trim();
                    actionSuggestion = summaryJson["actionSuggestion"]?.ToString()?.Trim()
                        ?? summaryJson["suggestion"]?.ToString()?.Trim();
                }
            }
            catch (Exception summaryEx)
            {
                _logger.LogWarning(summaryEx, "提取股票评级和操作建议失败");
            }

            // 保存到缓存（永久缓存，直到手动刷新）
            var analysisTime = DateTime.Now;
            var technicalChartResponse = !string.IsNullOrEmpty(technicalChartImageBase64)
                ? new
                {
                    imageBase64 = technicalChartImageBase64,
                    contentType = technicalChartContentType,
                    highlights = technicalChartHighlightsToken
                }
                : null;
            var cachedResult = new CachedAnalysisResult
            {
                Analysis = finalResult,
                AnalysisTime = analysisTime,
                StockCode = stockCode,
            StockName = stockNameForLog,
                AnalysisType = analysisType,
                TechnicalChartImageBase64 = technicalChartImageBase64,
                TechnicalChartContentType = technicalChartContentType,
                TechnicalChartHighlights = technicalChartHighlightsToken?.ToString(Newtonsoft.Json.Formatting.None),
                Rating = rating,
                ActionSuggestion = actionSuggestion
            };
            
            // 使用MemoryCacheEntryOptions设置缓存（不设置过期时间，永久缓存）
            var cacheOptions = new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.NeverRemove,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(2)
            };
            _cache.Set(cacheKey, cachedResult, cacheOptions);
            
        _logger.LogInformation("AI分析结果已缓存: {StockName} (分析类型: {AnalysisType}, 分析时间: {AnalysisTime})", 
            stockNameForLog, analysisType, analysisTime);
            
            // 返回JSON格式，包含分析结果
            return Ok(new { 
                success = true, 
                analysis = finalResult,
                length = finalResult.Length,
                sizeKB = Math.Round(responseSizeKB, 2),
                timestamp = analysisTime.ToString("yyyy-MM-dd HH:mm:ss"),
                cached = false,
                analysisTime = analysisTime.ToString("yyyy-MM-dd HH:mm:ss"),
                rating,
                actionSuggestion,
                technicalChart = technicalChartResponse
            });
    }

    /// <summary>
    /// 获取股票操作分析（一日做T、一周操作、一月操作）
    /// </summary>
    [HttpPost("analyze/{stockCode}/operation")]
    public async Task<ActionResult<OperationAnalysisResponse>> GetOperationAnalysis(
        string stockCode, 
        [FromBody] OperationAnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            return BadRequest(new { message = "股票代码不能为空", error = "INVALID_STOCK_CODE" });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.OperationType))
        {
            return BadRequest(new { message = "操作类型不能为空", error = "INVALID_OPERATION_TYPE" });
        }

        stockCode = stockCode.Trim().ToUpper();
        var operationType = request.OperationType.Trim().ToLowerInvariant();
        var forceRefresh = request.ForceRefresh;

        // 验证操作类型
        if (operationType != "day" && operationType != "week" && operationType != "month")
        {
            return BadRequest(new { message = "操作类型无效，必须是 day（一日做T）、week（一周操作）或 month（一月操作）", error = "INVALID_OPERATION_TYPE" });
        }

        // 构建操作分析缓存键
        var operationCacheKey = $"operation_analysis_{stockCode}_{operationType}";
        
        // 如果不需要强制刷新，先检查缓存
        if (!forceRefresh)
        {
            if (_cache.TryGetValue(operationCacheKey, out CachedOperationAnalysisResult? cachedOperationResult) && cachedOperationResult != null)
            {
                // 检查缓存是否过期
                var cacheExpiry = GetOperationCacheExpiry(operationType);
                var isExpired = DateTime.Now > cachedOperationResult.CacheTime.Add(cacheExpiry);
                
                if (!isExpired)
                {
                    _logger.LogInformation("使用缓存的操作分析结果: {StockCode}, {OperationType}, 缓存时间: {CacheTime}", 
                        stockCode, operationType, cachedOperationResult.CacheTime);
                    
                    return Ok(new OperationAnalysisResponse
                    {
                        Success = true,
                        StockCode = cachedOperationResult.StockCode,
                        StockName = cachedOperationResult.StockName,
                        OperationType = cachedOperationResult.OperationType,
                        OperationTypeName = cachedOperationResult.OperationTypeName,
                        Analysis = cachedOperationResult.Analysis,
                        AnalysisTime = cachedOperationResult.AnalysisTime,
                        BaseAnalysisTime = cachedOperationResult.BaseAnalysisTime,
                        BaseAnalysisType = cachedOperationResult.BaseAnalysisType,
                        CurrentPrice = cachedOperationResult.CurrentPrice,
                        ChangePercent = cachedOperationResult.ChangePercent,
                        Cached = true,
                        CacheTime = cachedOperationResult.CacheTime
                    });
                }
                else
                {
                    _logger.LogInformation("操作分析缓存已过期: {StockCode}, {OperationType}, 缓存时间: {CacheTime}, 过期时间: {Expiry}", 
                        stockCode, operationType, cachedOperationResult.CacheTime, cacheExpiry);
                }
            }
        }

        // 检查是否有AI分析结果（优先检查comprehensive类型）
        var analysisTypes = new[] { "comprehensive", "technical", "fundamental" };
        CachedAnalysisResult? cachedAnalysis = null;
        string? foundAnalysisType = null;

        foreach (var analysisType in analysisTypes)
        {
            var cacheKey = $"ai_analysis_{stockCode}_{analysisType}";
            if (_cache.TryGetValue(cacheKey, out CachedAnalysisResult? result) && result != null)
            {
                cachedAnalysis = result;
                foundAnalysisType = analysisType;
                break;
            }
        }

        if (cachedAnalysis == null || string.IsNullOrWhiteSpace(cachedAnalysis.Analysis))
        {
            return BadRequest(new 
            { 
                message = "该股票尚未进行AI分析，请先进行AI分析后再查看操作建议", 
                error = "NO_AI_ANALYSIS",
                requiresAnalysis = true
            });
        }

        // 获取股票基本信息
        Stock? stock = null;
        try
        {
            stock = await _stockDataService.GetRealTimeQuoteAsync(stockCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取股票信息失败: {StockCode}", stockCode);
        }

        var stockName = stock?.Name ?? cachedAnalysis.StockName ?? stockCode;
        var currentPrice = stock?.CurrentPrice ?? 0;
        var changePercent = stock?.ChangePercent ?? 0;

        // 构建操作分析提示词
        var operationTypeName = operationType switch
        {
            "day" => "一日做T",
            "week" => "一周操作",
            "month" => "一月操作",
            _ => "操作"
        };

        var timeFrame = operationType switch
        {
            "day" => "1个交易日",
            "week" => "5个交易日（一周）",
            "month" => "20个交易日（一个月）",
            _ => "短期"
        };

        // 根据AI分析结果的实际内容，构建客观的提示词
        var analysisSummary = cachedAnalysis.Analysis.Length > 1000 
            ? cachedAnalysis.Analysis.Substring(0, 1000) + "..."
            : cachedAnalysis.Analysis;

        var ratingInfo = !string.IsNullOrWhiteSpace(cachedAnalysis.Rating) 
            ? $"评级：{cachedAnalysis.Rating}" 
            : "";
        var suggestionInfo = !string.IsNullOrWhiteSpace(cachedAnalysis.ActionSuggestion) 
            ? $"操作建议：{cachedAnalysis.ActionSuggestion}" 
            : "";

        var operationPrompt = $@"基于以下AI分析结果，请为股票 {stockName}（代码：{stockCode}）提供{operationTypeName}的具体操作建议。

【当前市场情况】
- 当前价格：{currentPrice:F2}元
- 涨跌幅：{changePercent:F2}%
- 分析时间：{cachedAnalysis.AnalysisTime:yyyy-MM-dd HH:mm:ss}
{ratingInfo}
{suggestionInfo}

【AI分析结果摘要】
{analysisSummary}

【分析要求】
请基于上述AI分析结果，客观、理性地分析该股票在未来{timeFrame}内的操作策略。要求：

1. **客观评估**：根据AI分析中的实际情况（包括优势、风险、技术指标等），给出客观的操作建议，不得过于乐观或过于悲观。

2. **操作策略**：
   - 买入时机：如果AI分析显示有买入机会，请说明具体的买入时机和价格区间
   - 卖出时机：如果AI分析显示有卖出风险，请说明具体的卖出时机和价格区间
   - 持仓建议：说明是否适合持仓，以及持仓比例建议
   - 风险控制：明确止损位和止盈位

3. **风险提示**：必须明确指出可能的风险因素，包括但不限于：
   - 市场风险
   - 技术面风险
   - 基本面风险
   - 消息面风险

4. **操作要点**：
   - 关键价位：重要的支撑位和阻力位
   - 操作频率：适合的操作频率（如做T的频率）
   - 资金管理：建议的资金使用比例

请以结构化的方式输出，确保建议客观、可操作，避免过于乐观或过于悲观的表述。";

        try
        {
            var operationAnalysis = await _aiService.ExecutePromptAsync(
                promptName: null,
                userPrompt: operationPrompt,
                placeholders: null,
                modelId: request.ModelId
            );

            if (string.IsNullOrWhiteSpace(operationAnalysis))
            {
                return StatusCode(500, new { message = "生成操作分析失败，请稍后重试", error = "ANALYSIS_FAILED" });
            }

            var analysisTime = DateTime.Now;
            var response = new OperationAnalysisResponse
            {
                Success = true,
                StockCode = stockCode,
                StockName = stockName,
                OperationType = operationType,
                OperationTypeName = operationTypeName,
                Analysis = operationAnalysis,
                AnalysisTime = analysisTime,
                BaseAnalysisTime = cachedAnalysis.AnalysisTime,
                BaseAnalysisType = foundAnalysisType ?? "comprehensive",
                CurrentPrice = currentPrice,
                ChangePercent = changePercent,
                Cached = false,
                CacheTime = analysisTime
            };

            // 缓存操作分析结果
            var cacheExpiry = GetOperationCacheExpiry(operationType);
            var cachedResult = new CachedOperationAnalysisResult
            {
                StockCode = stockCode,
                StockName = stockName,
                OperationType = operationType,
                OperationTypeName = operationTypeName,
                Analysis = operationAnalysis,
                AnalysisTime = analysisTime,
                BaseAnalysisTime = cachedAnalysis.AnalysisTime,
                BaseAnalysisType = foundAnalysisType ?? "comprehensive",
                CurrentPrice = currentPrice,
                ChangePercent = changePercent,
                CacheTime = analysisTime
            };

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = cacheExpiry,
                Priority = CacheItemPriority.Normal
            };
            _cache.Set(operationCacheKey, cachedResult, cacheOptions);

            _logger.LogInformation("操作分析结果已缓存: {StockCode}, {OperationType}, 缓存时间: {CacheTime}, 过期时间: {Expiry}", 
                stockCode, operationType, analysisTime, cacheExpiry);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成操作分析失败: {StockCode}, {OperationType}", stockCode, operationType);
            return StatusCode(500, new { message = $"生成操作分析失败: {ex.Message}", error = "ANALYSIS_FAILED" });
        }
    }

    /// <summary>
    /// 聊天
    /// </summary>
    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { message = "请求不能为空", error = "EMPTY_REQUEST" });
        }

        var maxHistory = request.MaxHistory > 0 ? Math.Clamp(request.MaxHistory, 3, 10) : 5;
        var maxMessageCount = maxHistory * 2;

        var normalizedMessages = (request.Messages ?? new List<ChatMessageDto>())
            .Select(m => AiChatMessage.Create(m.Role, m.Content))
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .ToList();

        if (!normalizedMessages.Any())
        {
            return BadRequest(new { message = "请提供至少一条有效的聊天消息", error = "EMPTY_MESSAGES" });
        }

        if (normalizedMessages.Count > maxMessageCount)
        {
            normalizedMessages = normalizedMessages.Skip(normalizedMessages.Count - maxMessageCount).ToList();
        }

        var contextBuilder = new StringBuilder();

        // 如果提供了股票代码，总是获取实时股票数据（无论是否是第一次对话）
        if (!string.IsNullOrWhiteSpace(request.StockCode) && (request.ForceRealTimeData || request.IncludeAnalysisContext))
        {
            try
            {
                _logger.LogInformation("开始获取股票 {StockCode} 的实时数据上下文", request.StockCode);
                var realTimeData = await _aiService.GetStockRealTimeDataContextAsync(request.StockCode);
                if (!string.IsNullOrWhiteSpace(realTimeData))
                {
                    contextBuilder.AppendLine("=== 实时股票数据 ===");
                    contextBuilder.AppendLine(realTimeData);
                    contextBuilder.AppendLine();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取股票 {StockCode} 实时数据失败，但继续聊天", request.StockCode);
            }
        }

        if (request.IncludeAnalysisContext && !string.IsNullOrWhiteSpace(request.AnalysisSummary))
        {
            var stockCode = string.IsNullOrWhiteSpace(request.StockCode)
                ? "该股票"
                : request.StockCode.Trim().ToUpperInvariant();

            var analysisLabel = GetAnalysisTypeLabel(request.AnalysisType, request.AnalysisTypeLabel);

            contextBuilder.AppendLine($"=== 历史分析结果 ===");
            contextBuilder.AppendLine($"以下是股票 {stockCode} 的{analysisLabel}结果摘要，请结合实时数据和这些历史分析回答用户的提问：");
            contextBuilder.AppendLine(request.AnalysisSummary.Trim());
            contextBuilder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            if (contextBuilder.Length > 0)
            {
                contextBuilder.Append("请结合上述实时数据和历史分析回答：");
                contextBuilder.AppendLine();
            }
            contextBuilder.AppendLine(request.Context.Trim());
        }

        var context = contextBuilder.Length > 0 ? contextBuilder.ToString() : null;

        try
        {
            var reply = await _aiService.ChatAsync(normalizedMessages, context, request.ModelId, maxHistory);
            return Ok(new ChatResponse
            {
                Success = true,
                Reply = reply ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI聊天失败: {StockCode}", request.StockCode ?? "未知股票");
            return StatusCode(500, new ChatResponse
            {
                Success = false,
                Reply = $"AI聊天失败: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// 获取股票建议
    /// </summary>
    [HttpGet("recommend/{stockCode}")]
    public async Task<ActionResult<string>> GetRecommendation(string stockCode)
    {
        var result = await _aiService.GetStockRecommendationAsync(stockCode);
        return Ok(result);
    }

    private string GetAnalysisTypeLabel(string? analysisType, string? providedLabel)
    {
        if (!string.IsNullOrWhiteSpace(providedLabel))
        {
            return providedLabel.Trim();
        }

        return analysisType?.ToLowerInvariant() switch
        {
            "fundamental" => "基本面分析",
            "news" => "新闻舆论分析",
            "technical" => "技术面分析",
            _ => "综合分析"
        };
    }

    private string BuildRecommendationSummaryPrompt(string stockCode, string analysisContent)
    {
        return @$"你是一名资深投顾。请根据以下关于股票 {stockCode} 的分析内容，提炼评级与操作建议，并严格按照要求输出：
- 仅输出一个 JSON 对象，不要附加任何解释或注释。
- JSON 对象必须包含字段：""rating"" 和 ""actionSuggestion""。
- ""rating"" 必须从 ""优"", ""良"", ""中"", ""差"" 中选择。
- ""actionSuggestion"" 需给出简明的操作提示，限制在 10 个中文字符以内，不得包含标点符号，可参考 ""速买"", ""谨慎观望"", ""逢高减持"", ""果断止损"" 等表达。

分析内容如下（可能较长）：
<analysis>
{analysisContent}
</analysis>";
    }

    private void AppendNewsItems(StringBuilder builder, IEnumerable<FinancialNews> newsItems)
    {
        foreach (var newsItem in newsItems)
        {
            var publishTime = newsItem.PublishTime.ToString("yyyy-MM-dd HH:mm");
            builder.AppendLine($"- [{publishTime}] {newsItem.Source ?? "未知来源"}：{newsItem.Title ?? "无标题"}");
            if (!string.IsNullOrWhiteSpace(newsItem.Keywords))
            {
                builder.AppendLine($"  关键词：{newsItem.Keywords}");
            }

            var summaryText = !string.IsNullOrWhiteSpace(newsItem.Summary)
                ? newsItem.Summary
                : null;

            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                builder.AppendLine($"  摘要：{TrimContent(summaryText, 200)}");
            }

            if (!string.IsNullOrWhiteSpace(newsItem.Content))
            {
                builder.AppendLine($"  正文摘录：{TrimContent(newsItem.Content, summaryText == null ? 400 : 320)}");
            }
            if (!string.IsNullOrWhiteSpace(newsItem.Url))
            {
                builder.AppendLine($"  链接：{newsItem.Url}");
            }
        }
    }

    private async Task<List<FinancialNews>> GetIndustryRelatedNewsAsync(
        string industryName,
        IEnumerable<string>? candidateKeywords,
        IReadOnlyCollection<FinancialNews>? existingNews,
        int maxCount = 8)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(industryName))
        {
            keywords.Add(industryName.Trim());
        }

        if (candidateKeywords != null)
        {
            foreach (var keyword in candidateKeywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    keywords.Add(keyword.Trim());
                }
            }
        }

        if (keywords.Count == 0)
        {
            return new List<FinancialNews>();
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingNews != null)
        {
            foreach (var news in existingNews)
            {
                var key = !string.IsNullOrWhiteSpace(news.Url) ? news.Url : news.Title;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    seenKeys.Add(key);
                }
            }
        }

        _logger.LogDebug("行业新闻搜索功能已停用，仅保留按股票代码获取新闻。");
        return new List<FinancialNews>();
    }
    
    /// <summary>
    /// 从AKShare获取行业详情
    /// </summary>
    private async Task<IndustryInfoResult?> GetIndustryInfoFromAKShareAsync(string stockCode)
    {
        try
        {
            var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL")
                ?? "http://localhost:5001";

            var url = $"{pythonServiceUrl}/api/stock/industry/{stockCode}";

            _logger.LogDebug("尝试从Python服务获取行业详情: {Url}", url);

            using var pythonClient = new HttpClient();
            pythonClient.Timeout = TimeSpan.FromSeconds(120);
            pythonClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await pythonClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Python服务(AKShare)无法获取股票 {StockCode} 的行业数据", stockCode);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Python服务返回错误状态码: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonData = Newtonsoft.Json.Linq.JObject.Parse(responseContent);

            if (jsonData["success"]?.ToString() == "True" && jsonData["data"] != null)
            {
                var data = jsonData["data"] as Newtonsoft.Json.Linq.JObject;
                if (data != null)
                {
                    var industryName = data["industryName"]?.ToString() ?? "未知";
                    var industryCode = data["industryCode"]?.ToString() ?? string.Empty;
                    var industryDescription = data["description"]?.ToString() ?? string.Empty;
                    var industryStocks = data["stocks"] as Newtonsoft.Json.Linq.JArray;
                    var industryTrends = data["trends"]?.ToString() ?? string.Empty;
                    var industryPerformance = data["performance"] as Newtonsoft.Json.Linq.JObject;
                    var industryMarketData = data["marketData"] as Newtonsoft.Json.Linq.JObject;

                    var builder = new StringBuilder();
                    builder.AppendLine();
                    builder.AppendLine("【行业详情】（数据来源：AKShare - stock_board_industry_name_em）");
                    builder.AppendLine();
                    builder.AppendLine("**行业基本信息：**");
                    builder.AppendLine($"- 行业名称：{industryName}");
                    builder.AppendLine($"- 行业代码：{industryCode}");
                    if (!string.IsNullOrEmpty(industryDescription))
                    {
                        builder.AppendLine($"- 行业描述：{industryDescription}");
                    }
                    builder.AppendLine();

                    var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    static string? NormalizeKeyword(string? value)
                    {
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            return null;
                        }

                        var normalized = value.Replace("（", "(").Replace("）", ")");
                        var index = normalized.IndexOf('(');
                        if (index > 0)
                        {
                            normalized = normalized[..index];
                        }

                        normalized = normalized.Trim();
                        return normalized.Length >= 2 ? normalized : null;
                    }

                    void AddKeyword(string? value)
                    {
                        var normalized = NormalizeKeyword(value);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            keywords.Add(normalized);
                        }
                    }

                    void AddSplitKeywords(string? value)
                    {
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            return;
                        }

                        var separators = new[] { '/', '、', '-', '，', ',', ' ' };
                        foreach (var token in value.Split(separators, StringSplitOptions.RemoveEmptyEntries))
                        {
                            AddKeyword(token);
                        }
                    }

                    if (!string.Equals(industryName, "未知", StringComparison.OrdinalIgnoreCase))
                    {
                        AddKeyword(industryName);
                        AddSplitKeywords(industryName);
                    }

                    AddKeyword(industryCode);

                    if (industryMarketData != null && industryMarketData.Count > 0)
                    {
                        builder.AppendLine("**行业板块实时市场数据：**");

                        var latestPrice = industryMarketData["latestPrice"]?.ToString();
                        var changeAmount = industryMarketData["changeAmount"]?.ToString();
                        var changePercent = industryMarketData["changePercent"]?.ToString();
                        var totalMarketCap = industryMarketData["totalMarketCap"]?.ToString();
                        var turnoverRate = industryMarketData["turnoverRate"]?.ToString();
                        var risingCount = industryMarketData["risingCount"]?.ToString();
                        var fallingCount = industryMarketData["fallingCount"]?.ToString();
                        var leaderStock = industryMarketData["leaderStock"]?.ToString();
                        var leaderChangePercent = industryMarketData["leaderChangePercent"]?.ToString();

                        if (!string.IsNullOrEmpty(latestPrice) && latestPrice != "null")
                        {
                            builder.AppendLine($"- 行业板块指数：{latestPrice}");
                        }

                        if (!string.IsNullOrEmpty(changeAmount) && changeAmount != "null")
                        {
                            builder.AppendLine($"- 涨跌额：{changeAmount}");
                        }

                        if (!string.IsNullOrEmpty(changePercent) && changePercent != "null")
                        {
                            builder.AppendLine($"- 涨跌幅：{changePercent}%");
                        }

                        if (!string.IsNullOrEmpty(totalMarketCap) && totalMarketCap != "null")
                        {
                            if (decimal.TryParse(totalMarketCap, out var marketCapDecimal))
                            {
                                var marketCapBillion = marketCapDecimal / 1_000_000_000M;
                                builder.AppendLine($"- 行业总市值：{marketCapBillion:F2}亿元");
                            }
                            else
                            {
                                builder.AppendLine($"- 行业总市值：{totalMarketCap}");
                            }
                        }

                        if (!string.IsNullOrEmpty(turnoverRate) && turnoverRate != "null")
                        {
                            builder.AppendLine($"- 换手率：{turnoverRate}%");
                        }

                        if (!string.IsNullOrEmpty(risingCount) && risingCount != "null" &&
                            !string.IsNullOrEmpty(fallingCount) && fallingCount != "null")
                        {
                            builder.AppendLine($"- 上涨家数：{risingCount}，下跌家数：{fallingCount}");
                        }

                        if (!string.IsNullOrEmpty(leaderStock))
                        {
                            AddKeyword(leaderStock);
                            var leaderInfo = $"- 领涨股票：{leaderStock}";
                            if (!string.IsNullOrEmpty(leaderChangePercent) && leaderChangePercent != "null")
                            {
                                leaderInfo += $"（涨跌幅：{leaderChangePercent}%）";
                            }
                            builder.AppendLine(leaderInfo);
                        }

                        builder.AppendLine();
                    }

                    if (industryPerformance != null)
                    {
                        var avgPE = industryPerformance["avgPE"]?.ToString() ?? "N/A";
                        var avgPB = industryPerformance["avgPB"]?.ToString() ?? "N/A";
                        var avgROE = industryPerformance["avgROE"]?.ToString() ?? "N/A";
                        var totalMarketCapPerformance = industryPerformance["totalMarketCap"]?.ToString() ?? "N/A";
                        var avgChangePercent = industryPerformance["avgChangePercent"]?.ToString() ?? "N/A";

                        builder.AppendLine("**行业表现指标：**");
                        builder.AppendLine($"- 行业平均市盈率(PE)：{avgPE}");
                        builder.AppendLine($"- 行业平均市净率(PB)：{avgPB}");
                        builder.AppendLine($"- 行业平均ROE：{avgROE}");
                        builder.AppendLine($"- 行业总市值：{totalMarketCapPerformance}");
                        builder.AppendLine($"- 行业平均涨跌幅：{avgChangePercent}%");
                        builder.AppendLine();
                    }

                    if (!string.IsNullOrEmpty(industryTrends))
                    {
                        builder.AppendLine("**行业趋势分析：**");
                        builder.AppendLine(industryTrends);
                        builder.AppendLine();
                    }

                    if (industryStocks != null && industryStocks.Count > 0)
                    {
                        builder.AppendLine($"**行业内主要股票（共{industryStocks.Count}只）：**");
                        int displayCount = Math.Min(industryStocks.Count, 20);
                        for (int i = 0; i < displayCount; i++)
                        {
                            var stock = industryStocks[i] as Newtonsoft.Json.Linq.JObject;
                            if (stock != null)
                            {
                                var code = stock["code"]?.ToString() ?? string.Empty;
                                var name = stock["name"]?.ToString() ?? string.Empty;
                                var price = stock["price"]?.ToString() ?? "N/A";
                                var changePercent = stock["changePercent"]?.ToString() ?? "N/A";

                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    AddKeyword(name);
                                }

                                if (!string.IsNullOrWhiteSpace(code))
                                {
                                    AddKeyword(code);
                                }

                                builder.AppendLine($"- {name}({code}) 价格：{price}元 涨跌幅：{changePercent}%");
                            }
                        }

                        if (industryStocks.Count > displayCount)
                        {
                            builder.AppendLine($"... 还有{industryStocks.Count - displayCount}只股票未显示");
                        }

                        builder.AppendLine();
                    }

                    builder.AppendLine("**提示：请结合以上行业数据，分析该股票在所属行业中的地位、行业整体发展趋势，以及行业对该股票的影响。**");

                    var keywordList = keywords
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToList();

                    return new IndustryInfoResult
                    {
                        InfoText = builder.ToString(),
                        IndustryName = string.Equals(industryName, "未知", StringComparison.OrdinalIgnoreCase) ? null : industryName,
                        IndustryCode = string.IsNullOrWhiteSpace(industryCode) ? null : industryCode,
                        Keywords = keywordList
                    };
                }
            }

            return null;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            if (ex.Message.Contains("404") || ex.Message.Contains("NOT FOUND"))
            {
                _logger.LogDebug(ex, "Python服务返回404 - 股票代码 {StockCode} 的行业数据未找到", stockCode);
            }
            else
            {
                _logger.LogDebug(ex, "Python服务不可用（可能未启动）");
            }
            return null;
        }
        catch (System.Threading.Tasks.TaskCanceledException ex) when (ex.InnerException is System.TimeoutException || ex.Message.Contains("Timeout"))
        {
            _logger.LogWarning(ex, "Python服务请求超时");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Python服务调用失败");
            return null;
        }
    }
    
    /// <summary>
    /// 从AKShare获取个股人气榜数据
    /// </summary>
    private async Task<string> GetHotRankFromAKShareAsync(string stockCode)
    {
        try
        {
            var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                ?? "http://localhost:5001";
            
            var normalizedStockCode = (stockCode ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalizedStockCode))
            {
                return string.Empty;
            }

            var encodedStockCode = Uri.EscapeDataString(normalizedStockCode);
            var url = $"{pythonServiceUrl}/api/stock/hot-rank/{encodedStockCode}";
            
            _logger.LogDebug("尝试从Python服务获取个股人气榜数据: {Url}", url);
            
            using var pythonClient = new HttpClient();
            pythonClient.Timeout = TimeSpan.FromSeconds(120);
            pythonClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            var response = await pythonClient.GetAsync(url);
            
            // 如果返回404，说明数据未找到，返回空字符串
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Python服务(AKShare)无法获取个股人气榜数据");
                return "";
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Python服务返回错误状态码: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return "";
            }
            
            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonData = Newtonsoft.Json.Linq.JObject.Parse(responseContent);
            
            if (jsonData["success"]?.ToObject<bool>() == true)
            {
                var data = jsonData["data"] as Newtonsoft.Json.Linq.JObject;
                if (data == null)
                {
                    _logger.LogInformation("未从Python服务获取到有效的人气榜数据");
                    return "";
                }

                static string FormatChange(string? label, int? value)
                {
                    if (!value.HasValue)
                    {
                        return $"{label}: 暂无数据";
                    }

                    var sign = value.Value > 0 ? "+" : string.Empty;
                    return $"{label}: {sign}{value}";
                }

                int? ParseNullableInt(Newtonsoft.Json.Linq.JToken? token)
                {
                    if (token == null)
                    {
                        return null;
                    }

                    if (int.TryParse(token.ToString(), out var parsedInt))
                    {
                        return parsedInt;
                    }

                    if (double.TryParse(token.ToString(), out var parsedDouble))
                    {
                        return (int)Math.Round(parsedDouble);
                    }

                    return null;
                }

                var rank = ParseNullableInt(data["rank"]);
                var rankChange = ParseNullableInt(data["rankChange"]);
                var hisRankChange = ParseNullableInt(data["hisRankChange"]);
                var marketAllCount = ParseNullableInt(data["marketAllCount"]);
                var calcTime = data["calcTime"]?.ToString();
                var symbol = data["symbol"]?.ToString() ?? normalizedStockCode;
                var innerCode = data["innerCode"]?.ToString();

                var builder = new StringBuilder();
                builder.AppendLine();
                builder.AppendLine("【个股人气榜数据】（数据来源：AKShare - stock_hot_rank_latest_em）");
                if (!string.IsNullOrWhiteSpace(calcTime))
                {
                    builder.AppendLine($"更新时间：{calcTime}");
                }

                builder.AppendLine();

                if (rank.HasValue)
                {
                    var totalText = marketAllCount.HasValue ? $"/ 共{marketAllCount}只股票" : string.Empty;
                    builder.AppendLine($"**股票 {symbol} 当前人气排名: 第{rank}{totalText}**");
                    builder.AppendLine();
                    builder.AppendLine("**排名变化信息：**");
                    builder.AppendLine($"- {FormatChange("与上一期相比的排名变化", rankChange)}");
                    builder.AppendLine($"- {FormatChange("历史区间排名变化", hisRankChange)}");
                }
                else
                {
                    builder.AppendLine("当前未能获取到该股票的人气排名数据。");
                }

                if (!string.IsNullOrWhiteSpace(innerCode))
                {
                    builder.AppendLine();
                    builder.AppendLine($"内部代码：{innerCode}");
                }

                builder.AppendLine();
                builder.AppendLine("**提示：请结合人气排名及其变化，分析市场关注度与情绪趋势，对投资决策进行辅助判断。**");

                return builder.ToString();
            }

            return "";
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            if (ex.Message.Contains("404") || ex.Message.Contains("NOT FOUND"))
            {
                _logger.LogDebug(ex, "Python服务返回404 - 个股人气榜数据未找到");
            }
            else
            {
                _logger.LogDebug(ex, "Python服务不可用（可能未启动）");
            }
            return "";
        }
        catch (System.Threading.Tasks.TaskCanceledException ex) when (ex.InnerException is System.TimeoutException || ex.Message.Contains("Timeout"))
        {
            _logger.LogWarning(ex, "Python服务请求超时");
            return "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Python服务调用失败");
            return "";
        }
    }

    private static string TrimContent(string? content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "（无可用摘要）";
        }

        var trimmed = content.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        var safeLength = Math.Min(maxLength, trimmed.Length);
        return trimmed.Substring(0, safeLength) + "...";
    }

    private static string BuildFundamentalPrompt(string context)
    {
        return $@"请基于以下关于股票{{stockCode}}的基本面、行业及市场数据进行分析，并提供结构化投资建议：
{context}

请按以下结构输出：
1. 核心观点
2. 财务与成长性
3. 行业竞争与公司地位
4. 主要风险
5. 操作建议";
    }

    private static string BuildNewsPrompt(string context)
    {
        return $@"以下是与股票{{stockCode}}相关的新闻及舆论信息，请分析市场情绪与潜在影响，并给出风险提示：
{context}

请按以下结构输出：
1. 市场情绪与舆论方向
2. 关键事件及潜在影响
3. 行业或政策因素
4. 机会点
5. 风险提示与建议";
    }

    private static string BuildTechnicalPrompt(string context)
    {
        return $@"以下是股票{{stockCode}}的技术面与交易数据，请结合趋势、指标与量能进行分析，并给出操作建议：
{context}

请按以下结构输出：
1. 价格趋势与关键价位
2. 技术指标信号
3. 成交量与资金动向
4. 买卖信号与风险
5. 操作建议";
    }

    private static string ApplyPlaceholders(string template, IDictionary<string, string?> placeholders)
    {
        var result = template;
        foreach (var kv in placeholders)
        {
            if (!string.IsNullOrEmpty(kv.Key))
            {
                result = result.Replace(kv.Key, kv.Value ?? string.Empty);
            }
        }
        return result;
    }

    /// <summary>
    /// 获取操作分析的缓存过期时间
    /// </summary>
    private static TimeSpan GetOperationCacheExpiry(string operationType)
    {
        return operationType switch
        {
            "day" => TimeSpan.FromHours(1),      // 一日做T缓存1小时
            "week" => TimeSpan.FromDays(1),      // 一周操作缓存1天
            "month" => TimeSpan.FromDays(7),     // 一月操作缓存1周
            _ => TimeSpan.FromHours(1)
        };
    }

    private static string NormalizeStockCode(string? stockCode)
    {
        return string.IsNullOrWhiteSpace(stockCode)
            ? string.Empty
            : stockCode.Trim().ToUpperInvariant();
    }

    private (bool success, string? rating, string? suggestion, bool cached, string? analysisTime, string? errorMessage, string? analysis, JToken? technicalChart) ExtractAnalysisSummary(ActionResult<string> actionResult)
    {
        string? analysis = null;
        JToken? technicalChart = null;

        if (actionResult.Result is ObjectResult objectResult)
        {
            var statusCode = objectResult.StatusCode ?? 200;
            if (statusCode >= 400)
            {
                var error = objectResult.Value?.ToString() ?? $"HTTP {statusCode}";
                return (false, null, null, false, null, error, null, null);
            }

            if (objectResult.Value != null)
            {
                JObject payload = objectResult.Value is JObject jObject
                    ? jObject
                    : JObject.FromObject(objectResult.Value);

                var successFlag = payload["success"]?.Value<bool?>() ?? true;
                var rating = payload["rating"]?.ToString();
                var suggestion = payload["actionSuggestion"]?.ToString();
                var cached = payload["cached"]?.Value<bool?>() ?? false;
                var analysisTime = payload["analysisTime"]?.ToString() ?? payload["timestamp"]?.ToString();
                var message = payload["message"]?.ToString();
                analysis = payload["analysis"]?.ToString() ?? payload["result"]?.ToString();
                technicalChart = payload["technicalChart"];

                return (successFlag, rating, suggestion, cached, analysisTime, successFlag ? null : message, analysis, technicalChart);
            }

            return (true, null, null, false, null, null, analysis, technicalChart);
        }

        if (!string.IsNullOrWhiteSpace(actionResult.Value))
        {
            analysis = actionResult.Value;
            return (true, null, null, false, null, null, analysis, technicalChart);
        }

        return (false, null, null, false, null, "AI分析返回空结果", analysis, technicalChart);
    }

    private async Task<WatchlistCategory> EnsureTargetCategoryAsync(int? targetCategoryId, string? targetCategoryName)
    {
        var categories = await _watchlistService.GetCategoriesAsync();

        if (targetCategoryId.HasValue)
        {
            var category = categories.FirstOrDefault(c => c.Id == targetCategoryId.Value);
            if (category == null)
            {
                throw new InvalidOperationException($"未找到ID为 {targetCategoryId.Value} 的自选股分类");
            }
            return category;
        }

        var desiredName = string.IsNullOrWhiteSpace(targetCategoryName)
            ? "关注"
            : targetCategoryName.Trim();

        var existing = categories.FirstOrDefault(c =>
            string.Equals(c.Name, desiredName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            return existing;
        }

        var color = "#f97316";
        var description = "批量AI分析自动创建的关注分类";
        return await _watchlistService.CreateCategoryAsync(desiredName, description, color);
    }

}

public class ChatRequest
{
    public string? StockCode { get; set; }
    public string? AnalysisType { get; set; }
    public string? AnalysisTypeLabel { get; set; }
    public string? AnalysisSummary { get; set; }
    public bool IncludeAnalysisContext { get; set; } = true;
    public List<ChatMessageDto> Messages { get; set; } = new();
    public string? Context { get; set; }
    public int MaxHistory { get; set; } = 5;
    public int? ModelId { get; set; }
    public bool ForceRealTimeData { get; set; } = false; // 强制获取实时数据
}

public class ChatMessageDto
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public class ChatResponse
{
    public bool Success { get; set; }
    public string Reply { get; set; } = string.Empty;
}

public class AnalyzeRequest
{
    public int? PromptId { get; set; }
    public string? Context { get; set; }
    public int? ModelId { get; set; }
    public string? AnalysisType { get; set; } // 分析类型：comprehensive, fundamental, news, technical
    public bool ForceRefresh { get; set; } = false; // 是否强制刷新（跳过缓存）
}

/// <summary>
/// 缓存的AI分析结果
/// </summary>
public class CachedAnalysisResult
{
    public string Analysis { get; set; } = string.Empty;
    public DateTime AnalysisTime { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string AnalysisType { get; set; } = "comprehensive";
    public string? TechnicalChartImageBase64 { get; set; }
    public string? TechnicalChartContentType { get; set; }
    public string? TechnicalChartHighlights { get; set; }
    public string? Rating { get; set; }
    public string? ActionSuggestion { get; set; }
}

public class BatchAnalyzeRequest
{
    public List<string>? StockCodes { get; set; }
    public int? WatchlistCategoryId { get; set; }
    public int? TargetCategoryId { get; set; }
    public string? TargetCategoryName { get; set; }
    public int? Limit { get; set; }
    public string? AnalysisType { get; set; }
    public bool ForceRefresh { get; set; } = false;
}

public class BatchAnalyzeResponse
{
    public List<BatchAnalyzeItem> Items { get; set; } = new();
    public int TargetCategoryId { get; set; }
    public string TargetCategoryName { get; set; } = string.Empty;
}

public class BatchAnalyzeItem
{
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string? Rating { get; set; }
    public string? ActionSuggestion { get; set; }
    public bool AnalysisSucceeded { get; set; }
    public bool Cached { get; set; }
    public string? AnalysisTime { get; set; }
    public bool AddedToWatchlist { get; set; }
    public bool AlreadyInWatchlist { get; set; }
    public string? Message { get; set; }
    public string? Analysis { get; set; }
    public object? TechnicalChart { get; set; }
}

/// <summary>
/// 操作分析请求
/// </summary>
public class OperationAnalysisRequest
{
    /// <summary>
    /// 操作类型：day（一日做T）、week（一周操作）、month（一月操作）
    /// </summary>
    public string OperationType { get; set; } = string.Empty;
    
    /// <summary>
    /// 可选的模型ID
    /// </summary>
    public int? ModelId { get; set; }
    
    /// <summary>
    /// 是否强制刷新（忽略缓存）
    /// </summary>
    public bool ForceRefresh { get; set; } = false;
}

/// <summary>
/// 操作分析响应
/// </summary>
public class OperationAnalysisResponse
{
    public bool Success { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string OperationTypeName { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public DateTime AnalysisTime { get; set; }
    public DateTime BaseAnalysisTime { get; set; }
    public string BaseAnalysisType { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public decimal ChangePercent { get; set; }
    public bool Cached { get; set; } = false;
    public DateTime CacheTime { get; set; }
}

/// <summary>
/// 缓存的操作分析结果
/// </summary>
public class CachedOperationAnalysisResult
{
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string OperationTypeName { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public DateTime AnalysisTime { get; set; }
    public DateTime BaseAnalysisTime { get; set; }
    public string BaseAnalysisType { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public decimal ChangePercent { get; set; }
    public DateTime CacheTime { get; set; }
}

