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

    private readonly IIndustryService _industryService;
    private readonly IMarketService _marketService;

    public AIController(
        IAIService aiService,
        IStockDataService stockDataService,
        INewsService newsService,
        IWatchlistService watchlistService,
        IIndustryService industryService,
        IMarketService marketService,
        ILogger<AIController> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _aiService = aiService;
        _stockDataService = stockDataService;
        _newsService = newsService;
        _watchlistService = watchlistService;
        _industryService = industryService;
        _marketService = marketService;
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
            string? technicalChartImageBase64 = null;
            string technicalChartContentType = "image/png";
            JToken? technicalChartHighlightsToken = null;

            // 1. 尝试获取技术分析图表（UI展示需要）
            try 
            {
                var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                    ?? "http://localhost:5001";
                var analyzeUrl = $"{pythonServiceUrl}/api/stock/analyze/{stockCode}?months=3";
                
                using var pythonClient = new HttpClient();
                pythonClient.Timeout = TimeSpan.FromSeconds(30); 
                var analyzeResponse = await pythonClient.GetAsync(analyzeUrl);
                
                if (analyzeResponse.IsSuccessStatusCode)
                {
                    var analyzeContent = await analyzeResponse.Content.ReadAsStringAsync();
                    var analyzeJson = JObject.Parse(analyzeContent);
                    if (analyzeJson["success"]?.ToString() == "True" && analyzeJson["data"] != null)
                    {
                        var chart = analyzeJson["data"]?["chart"];
                        if (chart != null)
                        {
                            technicalChartImageBase64 = chart["imageBase64"]?.ToString();
                            technicalChartHighlightsToken = chart["highlights"];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("获取技术图表失败（不影响主流程）: {Message}", ex.Message);
            }

            // 2. 根据分析类型构建上下文指令
            string specificInstruction = analysisType switch
            {
                "fundamental" => "请重点进行基本面分析，关注财务状况、行业地位和估值水平。",
                "technical" => "请重点进行技术面分析，关注价格走势、成交量变化和技术指标信号。",
                "news" => "请重点进行消息面分析，关注近期新闻、公告和舆情影响。",
                _ => "请进行全方位的综合分析，涵盖基本面、技术面和消息面，并给出明确的投资建议。"
            };

            // 3. 调用 Agentic AI Service 进行分析 (AI将自主调用工具获取数据)
            string finalResult = await _aiService.AnalyzeStockAsync(
                stockCode,
                request?.PromptId,
                specificInstruction,
                request?.ModelId
            );

            // 4. 解析评级和建议 (Extract Rating/Suggestion)
            string? rating = null;
            string? actionSuggestion = null;
            try
            {
                var recommendationPrompt = BuildRecommendationSummaryPrompt(stockCode, finalResult);
                // 这里我们复用 ExecutePromptAsync 来做纯文本处理，不需要工具
                var recommendationResponse = await _aiService.ExecutePromptAsync(
                    null,
                    recommendationPrompt,
                    null,
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
                
            double responseSizeKB = (finalResult?.Length ?? 0) * 2 / 1024.0;
            
            // 返回JSON格式，包含分析结果
            return Ok(new { 
                success = true, 
                analysis = finalResult,
                length = finalResult?.Length ?? 0,
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
            .Select(m => new AiChatMessage 
            { 
                Role = string.IsNullOrWhiteSpace(m.Role) ? "user" : m.Role.Trim().ToLowerInvariant(),
                Content = m.Content,
                ToolCalls = m.ToolCalls,
                ToolCallId = m.ToolCallId
            })
            .Where(m => !string.IsNullOrWhiteSpace(m.Content) || (m.ToolCalls != null && m.ToolCalls.Any()))
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
- ""rating"" 必须是 1-100 之间的整数，表示综合评分。评分标准：80-100分为优秀，60-79分为良好，40-59分为中等，0-39分为较差。请综合考虑基本面、技术面、市场情绪等因素给出客观评分。
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
        return $@"你是一名专业的证券分析师，请基于我提供的财务数据、行业信息和公司资料，
对一只A股股票进行【七日短线策略背景下的基本面分析】。

相关数据如下：
{context}

请按照以下结构化格式输出：

1. 公司核心业务概况（不超过80字）
2. 行业景气度（高 / 中 / 低，并解释原因）
3. 公司竞争优势（列出1–3条）
4. 风险因素（列出1–3条，如负债高、行业衰退等）
5. 财务稳健性评分（0-10）
   - 盈利能力
   - 偿债能力
   - 成长性
   - 现金流质量
   - 机构持仓趋势
6. 是否适合作为短线标的（是 / 否）
   - 不是预测未来股价，而是判断其短线安全性（流动性、行业热度、机构关注等）
7. 最终结论（简要总结一句）

请务必避免预测未来涨跌，只做数据分析和风险判断。";
    }

    private static string BuildNewsPrompt(string context)
    {
        return $@"你是一名证券舆情分析师。
我将提供一条与某只A股相关的新闻、公告、研报或政策内容。

相关信息如下：
{context}

请按照以下结构化格式输出分析结果：

1. 事件类型（利好 / 利空 / 中性）
2. 事件所属类别：
   - 业绩类（预增、预亏）
   - 产能类（扩产、投产）
   - 订单类（新增订单）
   - 政策类（产业支持、监管）
   - 股权类（减持、增持）
   - 其他（请说明）
3. 对公司基本面的影响（实质 / 偏弱 / 无实质，请解释）
4. 对行业情绪的影响（提升 / 较弱 / 中性）
5. 是否属于短期炒作题材（是 / 否，并说明逻辑）
6. 风险点（监管、兑现预期、数据不实、情绪过度等）
7. 新闻情绪评分（0-10）
8. 是否适合作为短线辅助判断（是 / 否；不是预测股价）

请只做事件影响分析，不进行任何形式的涨跌预测。";
    }

    private static string BuildTechnicalPrompt(string context)
    {
        return $@"你是一名专业的短线交易技术面分析师。
我将提供某只A股最近N日的K线（开盘价、收盘价、最高价、最低价、成交量）、
以及MA5/10/20、MACD、RSI、换手率等基础指标。

相关数据如下：
{context}

请按照以下格式输出技术分析结果：

1. 当前短期趋势方向（上涨 / 震荡 / 下跌，并给出逻辑）
2. 突破/跌破关键信号：
   - 是否突破MA5 / MA10
   - 是否回踩确认成功
   - MACD是否金叉/死叉
   - RSI是否处在超买或超卖区域
3. 成交量结构判断：
   - 最近3日 vs 最近10日（成交量放大或萎缩）
   - 主力资金活跃度（从成交量变化和换手率推断）
4. 形态结构（若存在）：
   - 突破、回踩、加速、缩量、双底、箱体、趋势线等
5. 短线风险（列1–3条，如缩量、加速、高位、阴包阳等）
6. 短线参与评分（0-10）
   - 趋势强度
   - 量价配合
   - 波动结构
   - 热点匹配度（如有输入）
7. 短线策略建议（不预测股价；只说明更偏向观察/跟踪/轻仓试错）

请严格避免做未来价格预测，只分析当前结构和风险。";
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
    public string? Content { get; set; }
    public List<AiToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
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

