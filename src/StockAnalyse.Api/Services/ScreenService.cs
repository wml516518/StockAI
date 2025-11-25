using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using StockAnalyse.Api.Services.Abstractions;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StockAnalyse.Api.Services;

public class ScreenService : IScreenService
{
    private readonly StockDbContext _context;
    private readonly ILogger<ScreenService> _logger;
    private readonly IStockDataService _stockDataService;
    private readonly IMemoryCache _cache;
    private readonly IAIService _aiService;
    private const int CacheExpirationMinutes = 10; // 缓存10分钟

    public ScreenService(
        StockDbContext context, 
        ILogger<ScreenService> logger,
        IStockDataService stockDataService,
        IMemoryCache cache,
        IAIService aiService)
    {
        _context = context;
        _logger = logger;
        _stockDataService = stockDataService;
        _cache = cache;
        _aiService = aiService;
    }

    /// <summary>
    /// 生成查询条件的缓存键
    /// </summary>
    private string GenerateCacheKey(ScreenCriteria criteria)
    {
        // 排除分页参数，只使用筛选条件生成缓存键
        var criteriaForCache = new
        {
            criteria.Market,
            criteria.MinPrice,
            criteria.MaxPrice,
            criteria.MinChangePercent,
            criteria.MaxChangePercent,
            criteria.MinTurnoverRate,
            criteria.MaxTurnoverRate,
            criteria.MinVolume,
            criteria.MaxVolume,
            criteria.MinMarketValue,
            criteria.MaxMarketValue,
            criteria.MinDividendYield,
            criteria.MaxDividendYield,
            criteria.MinPE,
            criteria.MaxPE,
            criteria.MinPB,
            criteria.MaxPB,
            criteria.MinCirculatingShares,
            criteria.MaxCirculatingShares,
            criteria.MinTotalShares,
            criteria.MaxTotalShares
        };
        
        var json = JsonSerializer.Serialize(criteriaForCache);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
        return $"ScreenResult_{Convert.ToBase64String(hashBytes)}";
    }

    public async Task<PagedResult<Stock>> ScreenStocksAsync(ScreenCriteria criteria)
    {
        // 生成缓存键（基于筛选条件，不包括分页参数）
        var cacheKey = GenerateCacheKey(criteria);
        
        // 尝试从缓存获取全部筛选结果
        List<Stock> allResults;
        
        // 如果强制刷新，跳过缓存，直接获取最新数据
        if (criteria.ForceRefresh)
        {
            _logger.LogInformation("强制刷新，跳过缓存，重新从接口获取数据，缓存键: {CacheKey}", cacheKey);
            allResults = await ScreenStocksAllAsync(criteria);
            
            // 更新缓存（即使强制刷新，也更新缓存供后续翻页使用）
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
                SlidingExpiration = TimeSpan.FromMinutes(5), // 滑动过期5分钟
                Priority = CacheItemPriority.Normal
            };
            _cache.Set(cacheKey, allResults, cacheOptions);
            _logger.LogInformation("选股结果已更新缓存，缓存键: {CacheKey}, 记录数: {Count}, 过期时间: {Expiration}分钟", 
                cacheKey, allResults.Count, CacheExpirationMinutes);
        }
        else if (_cache.TryGetValue<List<Stock>>(cacheKey, out var cachedResults))
        {
            // 使用缓存
            _logger.LogInformation("从缓存获取选股结果，缓存键: {CacheKey}, 记录数: {Count}", cacheKey, cachedResults.Count);
            allResults = cachedResults;
        }
        else
        {
            // 缓存不存在，执行完整查询
            _logger.LogInformation("缓存未命中，开始执行完整查询，缓存键: {CacheKey}", cacheKey);
            allResults = await ScreenStocksAllAsync(criteria);
            
            // 将结果存入缓存（10分钟过期）
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
                SlidingExpiration = TimeSpan.FromMinutes(5), // 滑动过期5分钟
                Priority = CacheItemPriority.Normal
            };
            _cache.Set(cacheKey, allResults, cacheOptions);
            _logger.LogInformation("选股结果已缓存，缓存键: {CacheKey}, 记录数: {Count}, 过期时间: {Expiration}分钟", 
                cacheKey, allResults.Count, CacheExpirationMinutes);
        }
        
        // 从缓存的结果中应用分页
        var pageIndex = Math.Max(1, criteria.PageIndex);
        var pageSize = Math.Max(1, Math.Min(100, criteria.PageSize)); // 限制每页最多100条
        
        var skip = (pageIndex - 1) * pageSize;
        var pagedItems = allResults.Skip(skip).Take(pageSize).ToList();
        
        _logger.LogInformation("分页查询完成 - 总记录数: {TotalCount}, 页码: {PageIndex}, 每页: {PageSize}, 返回: {ReturnCount} 条", 
            allResults.Count, pageIndex, pageSize, pagedItems.Count);
        
        return new PagedResult<Stock>
        {
            Items = pagedItems,
            TotalCount = allResults.Count,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<List<Stock>> ScreenStocksAllAsync(ScreenCriteria criteria)
    {
        _logger.LogInformation("开始条件选股，条件：{Criteria}", 
            System.Text.Json.JsonSerializer.Serialize(criteria));
        
        // 从东方财富接口获取股票数据，而不是从数据库查询
        _logger.LogInformation("开始从东方财富获取股票数据...");
        List<Stock> allStocks;
        
        try
        {
            // 根据市场筛选条件确定要获取的市场
            string? marketParam = null;
            if (!string.IsNullOrEmpty(criteria.Market))
            {
                // SH -> 上交所, SZ -> 深交所
                marketParam = criteria.Market;
            }
            
            // 优先使用腾讯财经接口（数据更准确），失败时回退到东方财富
            try
            {
                allStocks = await _stockDataService.FetchAllStocksFromTencentAsync(marketParam, 2000);
                if (allStocks.Count == 0)
                {
                    throw new Exception("腾讯财经接口返回空数据");
                }
            }
            catch (Exception tencentEx)
            {
                _logger.LogWarning(tencentEx, "腾讯财经接口失败，尝试使用东方财富接口");
                allStocks = await _stockDataService.FetchAllStocksFromEastMoneyAsync(marketParam);
            }
            _logger.LogInformation("获取到 {Count} 只股票", allStocks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从东方财富获取股票数据失败，回退到数据库查询");
            // 如果接口失败，回退到数据库查询
            allStocks = await _context.Stocks.ToListAsync();
            _logger.LogInformation("从数据库获取到 {Count} 只股票（回退模式）", allStocks.Count);
        }
        
        if (allStocks.Count == 0)
        {
            _logger.LogWarning("未获取到任何股票数据");
            return new List<Stock>();
        }
        
        // 统计初始数据情况
        var priceRange = allStocks.Where(s => s.CurrentPrice > 0).Select(s => s.CurrentPrice);
        var minPrice = priceRange.Any() ? priceRange.Min() : 0;
        var maxPrice = priceRange.Any() ? priceRange.Max() : 0;
        
        _logger.LogInformation("初始数据统计 - 总股票数: {Total}, 价格范围: {MinPrice}-{MaxPrice}", 
            allStocks.Count, minPrice, maxPrice);
        
        // 应用筛选条件
        var query = allStocks.AsQueryable();
        int previousCount = allStocks.Count;
        
        // 市场筛选（已在获取数据时处理，这里再次确认）
        if (!string.IsNullOrEmpty(criteria.Market))
        {
            query = query.Where(s => s.Market == criteria.Market);
            var afterMarketCount = query.Count();
            _logger.LogInformation("市场筛选后股票数: {Count} -> {NewCount} (市场: {Market})", 
                previousCount, afterMarketCount, criteria.Market);
            previousCount = afterMarketCount;
        }
        
        // 价格条件
        if (criteria.MinPrice.HasValue)
        {
            query = query.Where(s => s.CurrentPrice >= criteria.MinPrice.Value);
            var count = query.Count();
            _logger.LogInformation("应用最低价格条件(>={MinPrice})后股票数: {PreviousCount} -> {NewCount}", 
                criteria.MinPrice.Value, previousCount, count);
            previousCount = count;
        }
        if (criteria.MaxPrice.HasValue)
        {
            query = query.Where(s => s.CurrentPrice <= criteria.MaxPrice.Value);
            var count = query.Count();
            _logger.LogInformation("应用最高价格条件(<={MaxPrice})后股票数: {PreviousCount} -> {NewCount}", 
                criteria.MaxPrice.Value, previousCount, count);
            previousCount = count;
        }
        
        // 涨跌幅条件
        if (criteria.MinChangePercent.HasValue)
        {
            query = query.Where(s => s.ChangePercent >= criteria.MinChangePercent.Value);
            var count = query.Count();
            _logger.LogInformation("应用最低涨跌幅条件(>={MinChange}%)后股票数: {PreviousCount} -> {NewCount}", 
                criteria.MinChangePercent.Value, previousCount, count);
            previousCount = count;
        }
        if (criteria.MaxChangePercent.HasValue)
        {
            query = query.Where(s => s.ChangePercent <= criteria.MaxChangePercent.Value);
            var count = query.Count();
            _logger.LogInformation("应用最高涨跌幅条件(<={MaxChange}%)后股票数: {PreviousCount} -> {NewCount}", 
                criteria.MaxChangePercent.Value, previousCount, count);
            previousCount = count;
        }
        
        // 换手率条件
        if (criteria.MinTurnoverRate.HasValue)
        {
            query = query.Where(s => s.TurnoverRate >= criteria.MinTurnoverRate.Value);
            var count = query.Count();
            _logger.LogInformation("应用最低换手率条件(>={MinTurnover}%)后股票数: {PreviousCount} -> {NewCount}", 
                criteria.MinTurnoverRate.Value, previousCount, count);
            previousCount = count;
        }
        if (criteria.MaxTurnoverRate.HasValue)
        {
            query = query.Where(s => s.TurnoverRate <= criteria.MaxTurnoverRate.Value);
            var count = query.Count();
            _logger.LogInformation("应用最高换手率条件(<={MaxTurnover}%)后股票数: {PreviousCount} -> {NewCount}", 
                criteria.MaxTurnoverRate.Value, previousCount, count);
            previousCount = count;
        }
        
        // 成交量条件
        if (criteria.MinVolume.HasValue)
        {
            query = query.Where(s => s.Volume >= criteria.MinVolume.Value);
            var count = query.Count();
            _logger.LogInformation("应用最低成交量条件(>={MinVolume})后股票数: {PreviousCount} -> {NewCount}", 
                criteria.MinVolume.Value, previousCount, count);
            previousCount = count;
        }
        if (criteria.MaxVolume.HasValue)
        {
            query = query.Where(s => s.Volume <= criteria.MaxVolume.Value);
            var count = query.Count();
            _logger.LogInformation("应用最高成交量条件(<={MaxVolume})后股票数: {PreviousCount} -> {NewCount}", 
                criteria.MaxVolume.Value, previousCount, count);
            previousCount = count;
        }
        
        // MACD条件（已移除，因为MACD字段已被删除）
        // if (criteria.MACDCrossUp.HasValue && criteria.MACDCrossUp.Value)
        // {
        //     query = query.Where(s => s.MACD > s.Signal && s.Histogram > 0);
        // }
        // if (criteria.MACDCrossDown.HasValue && criteria.MACDCrossDown.Value)
        // {
        //     query = query.Where(s => s.MACD < s.Signal && s.Histogram < 0);
        // }
        
        var results = query.ToList();
        var beforePostFilter = results.Count;
        _logger.LogInformation("应用所有数据库筛选条件后得到 {Count} 条结果", beforePostFilter);
        
        // 后置过滤条件（需要计算的指标）
        var beforeMarketValueFilter = results.Count;
        results = results.Where(stock => {
            // 市值条件（单位：万元）
            // 注意：由于 Stock 模型中没有总股本数据，无法准确计算市值
            // 这里使用成交额和换手率来估算流通市值：流通市值 ≈ 成交额 / (换手率/100)
            if (criteria.MinMarketValue.HasValue || criteria.MaxMarketValue.HasValue)
            {
                decimal estimatedMarketValue = 0;
                
                // 方法1：使用成交额和换手率估算（更准确）
                // 流通市值 = 成交额 / (换手率/100)
                // 假设成交额单位是元，需要转换为万元
                if (stock.TurnoverRate > 0 && stock.Turnover > 0)
                {
                    // 流通市值（元）= 成交额（元）/ (换手率/100)
                    decimal marketValueYuan = stock.Turnover / (stock.TurnoverRate / 100m);
                    // 转换为万元
                    estimatedMarketValue = marketValueYuan / 10000;
                }
                
                // 方法2：如果方法1无法使用，使用价格估算（作为备选，但不准确）
                if (estimatedMarketValue == 0 || estimatedMarketValue < 1)
                {
                    // 使用一个更合理的估算：假设平均流通股本约为30000万股（30亿股）
                    // 流通市值（万元）= 股价（元）* 流通股本（万股）
                    // 对于中小盘成长股，流通股本通常在20-50亿股之间
                    estimatedMarketValue = stock.CurrentPrice * 30000;
                }
                
                if (criteria.MinMarketValue.HasValue && estimatedMarketValue < criteria.MinMarketValue.Value)
                {
                    return false;
                }
                if (criteria.MaxMarketValue.HasValue && estimatedMarketValue > criteria.MaxMarketValue.Value)
                {
                    return false;
                }
            }
            
            // 股息率条件（如果有此字段的话）
            // 这里可以添加股息率的后置过滤
            
            return true;
        }).ToList();
        
        var marketValueFilteredCount = beforeMarketValueFilter - results.Count;
        
        var finalCount = results.Count;
        
        if (marketValueFilteredCount > 0)
        {
            _logger.LogInformation("市值条件过滤: {FilteredCount} 条记录", marketValueFilteredCount);
        }
        
        _logger.LogInformation("条件选股查询完成 - 最终返回 {FinalCount} 条结果 (筛选前: {BeforePostFilter}, 数据源: {SourceCount})", 
            finalCount, beforePostFilter, allStocks.Count);
        
        if (finalCount == 0 && allStocks.Count > 0)
        {
            // 提供诊断信息
            var diagnosticInfo = new System.Text.StringBuilder();
            diagnosticInfo.AppendLine($"数据源总股票数: {allStocks.Count}");
            
            _logger.LogWarning("选股结果为空，诊断信息:\n{Diagnostics}", diagnosticInfo.ToString());
        }
        
        return results;
    }

    public async Task<string> GetShortTermHotStrategyAsync(int topHot, int topThemes, int themeMembers)
    {
        try
        {
            var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL")
                ?? "http://localhost:5001";

            var url = $"{pythonServiceUrl}/api/strategy/hot-volume-breakout?topHot={topHot}&topThemes={topThemes}&themeMembers={themeMembers}";

            _logger.LogInformation("请求Python短线策略接口: {Url}", url);

            using var pythonClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(300)
            };
            pythonClient.DefaultRequestHeaders.Add("User-Agent", "StockAnalyse.Api/1.0");

            var response = await pythonClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Python短线策略接口返回错误状态码 {StatusCode}: {Content}", response.StatusCode, content);
                throw new InvalidOperationException($"Python服务调用失败: {response.StatusCode}");
            }

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取短线策略结果失败");
            throw;
        }
    }

    public async Task<ScreenCriteria> ParseNaturalLanguageToCriteriaAsync(string naturalLanguage, int? modelId = null)
    {
        if (string.IsNullOrWhiteSpace(naturalLanguage))
        {
            _logger.LogWarning("自然语言选股条件为空");
            return new ScreenCriteria { PageIndex = 1, PageSize = 10 };
        }

        _logger.LogInformation("开始解析自然语言选股条件: {NaturalLanguage}", naturalLanguage);

        // 构建AI提示词，要求AI将自然语言转换为JSON格式的选股条件
        var systemPrompt = @"你是一位专业的股票筛选助手。用户会用自然语言描述选股条件，你需要将其转换为结构化的JSON格式。

可用的选股条件字段：
- Market: 市场类型，可选值：""SH""（上海市场）、""SZ""（深圳市场），空字符串表示全部市场
- MinPrice/MaxPrice: 价格区间（元）
- MinChangePercent/MaxChangePercent: 涨跌幅区间（%）
- MinTurnoverRate/MaxTurnoverRate: 换手率区间（%）
- MinVolume/MaxVolume: 成交量区间（手）
- MinMarketValue/MaxMarketValue: 市值区间（万元）
- MinPE/MaxPE: 市盈率区间
- MinPB/MaxPB: 市净率区间
- MinDividendYield/MaxDividendYield: 股息率区间（%）

请仔细分析用户的自然语言描述，提取所有相关的数值和条件，然后返回一个JSON对象，只包含用户明确提到的条件字段。
如果用户没有提到某个条件，该字段应该为null或省略。
数值单位要正确转换（例如：换手率5%应该转换为5.0，市值100亿应该转换为1000000万元）。

返回格式必须是纯JSON，不要包含任何解释文字，格式如下：
{
  ""Market"": ""SH"" 或 ""SZ"" 或 null,
  ""MinPrice"": 数值 或 null,
  ""MaxPrice"": 数值 或 null,
  ""MinChangePercent"": 数值 或 null,
  ""MaxChangePercent"": 数值 或 null,
  ""MinTurnoverRate"": 数值 或 null,
  ""MaxTurnoverRate"": 数值 或 null,
  ""MinVolume"": 数值 或 null,
  ""MaxVolume"": 数值 或 null,
  ""MinMarketValue"": 数值 或 null,
  ""MaxMarketValue"": 数值 或 null,
  ""MinPE"": 数值 或 null,
  ""MaxPE"": 数值 或 null,
  ""MinPB"": 数值 或 null,
  ""MaxPB"": 数值 或 null,
  ""MinDividendYield"": 数值 或 null,
  ""MaxDividendYield"": 数值 或 null,
  ""PageIndex"": 1,
  ""PageSize"": 10
}

示例：
用户输入：""换手率大于5%的股票""
返回：{""MinTurnoverRate"": 5.0, ""PageIndex"": 1, ""PageSize"": 10}

用户输入：""上海市场，价格在10到50元之间，涨跌幅在-5%到10%之间""
返回：{""Market"": ""SH"", ""MinPrice"": 10.0, ""MaxPrice"": 50.0, ""MinChangePercent"": -5.0, ""MaxChangePercent"": 10.0, ""PageIndex"": 1, ""PageSize"": 10}";

        var userPrompt = $"请将以下自然语言描述转换为选股条件的JSON格式：\n\n{naturalLanguage}";

        try
        {
            // 调用AI服务解析自然语言，使用ChatAsync方法以便传递systemPrompt
            var messages = new List<AiChatMessage>
            {
                new("system", systemPrompt),
                new("user", userPrompt)
            };

            var aiResponse = await _aiService.ChatAsync(messages, context: null, modelId: modelId, maxHistory: 2);

            _logger.LogInformation("AI解析结果: {AiResponse}", aiResponse);

            // 检查AI是否返回错误消息
            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                _logger.LogWarning("AI返回空响应");
                throw new InvalidOperationException("AI返回空响应，请检查AI配置");
            }

            // 检查是否是错误消息（通常以"请先"、"失败"、"错误"等开头）
            if (aiResponse.Contains("请先") || 
                aiResponse.Contains("失败") || 
                aiResponse.Contains("错误") ||
                aiResponse.Contains("AI调用失败") ||
                aiResponse.Contains("AI返回结构异常") ||
                aiResponse.Contains("配置错误"))
            {
                _logger.LogWarning("AI返回错误消息: {AiResponse}", aiResponse);
                throw new InvalidOperationException($"AI服务错误: {aiResponse}");
            }

            // 尝试从AI响应中提取JSON
            var jsonText = ExtractJsonFromResponse(aiResponse);
            
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                _logger.LogWarning("无法从AI响应中提取JSON，AI响应: {AiResponse}", aiResponse);
                throw new InvalidOperationException($"无法从AI响应中提取JSON格式的选股条件。AI响应: {aiResponse}");
            }

            // 反序列化为ScreenCriteria
            var criteria = System.Text.Json.JsonSerializer.Deserialize<ScreenCriteria>(jsonText, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            });

            if (criteria == null)
            {
                _logger.LogWarning("JSON反序列化失败，JSON文本: {JsonText}", jsonText);
                throw new InvalidOperationException($"JSON反序列化失败，无法解析选股条件。JSON: {jsonText}");
            }

            // 确保分页参数有效
            criteria.PageIndex = Math.Max(1, criteria.PageIndex);
            criteria.PageSize = Math.Max(1, Math.Min(100, criteria.PageSize));

            _logger.LogInformation("成功解析自然语言选股条件: {Criteria}", 
                System.Text.Json.JsonSerializer.Serialize(criteria));

            return criteria;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI解析自然语言选股条件失败: {Message}", ex.Message);
            // 重新抛出异常，让Controller处理
            throw;
        }
    }

    /// <summary>
    /// 从AI响应中提取JSON内容
    /// </summary>
    private string ExtractJsonFromResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        // 尝试直接解析整个响应
        response = response.Trim();

        // 如果响应以{开头，尝试找到第一个{和最后一个}
        var startIndex = response.IndexOf('{');
        if (startIndex < 0)
            return string.Empty;

        var endIndex = response.LastIndexOf('}');
        if (endIndex < startIndex)
            return string.Empty;

        var jsonText = response.Substring(startIndex, endIndex - startIndex + 1);

        // 验证是否是有效的JSON
        try
        {
            System.Text.Json.JsonDocument.Parse(jsonText);
            return jsonText;
        }
        catch
        {
            _logger.LogWarning("提取的文本不是有效的JSON: {JsonText}", jsonText);
            return string.Empty;
        }
    }
}

