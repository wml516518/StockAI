using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class ScreenService : IScreenService
{
    private readonly StockDbContext _context;
    private readonly ILogger<ScreenService> _logger;
    private readonly IStockDataService _stockDataService;

    public ScreenService(
        StockDbContext context, 
        ILogger<ScreenService> logger,
        IStockDataService stockDataService)
    {
        _context = context;
        _logger = logger;
        _stockDataService = stockDataService;
    }

    public async Task<List<Stock>> ScreenStocksAsync(ScreenCriteria criteria)
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
        results = results.Where(stock => {
            // 市值条件（假设股价*总股本=市值，单位：万元）
            if (criteria.MinMarketValue.HasValue || criteria.MaxMarketValue.HasValue)
            {
                // 这里需要根据实际情况调整计算方式
                // 暂时使用一个近似计算：当前价格 * 10000（假设总股本为10000万股）
                decimal marketValue = stock.CurrentPrice * 10000;
                if (criteria.MinMarketValue.HasValue && marketValue < criteria.MinMarketValue.Value)
                    return false;
                if (criteria.MaxMarketValue.HasValue && marketValue > criteria.MaxMarketValue.Value)
                    return false;
            }
            
            // 股息率条件（如果有此字段的话）
            // 这里可以添加股息率的后置过滤
            
            return true;
        }).ToList();
        
        var finalCount = results.Count;
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

