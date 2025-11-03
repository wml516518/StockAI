using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
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
    private const int CacheExpirationMinutes = 10; // 缓存10分钟

    public ScreenService(
        StockDbContext context, 
        ILogger<ScreenService> logger,
        IStockDataService stockDataService,
        IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _stockDataService = stockDataService;
        _cache = cache;
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
}

