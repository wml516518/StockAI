using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class ScreenService : IScreenService
{
    private readonly StockDbContext _context;
    private readonly ILogger<ScreenService> _logger;

    public ScreenService(StockDbContext context, ILogger<ScreenService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<Stock>> ScreenStocksAsync(ScreenCriteria criteria)
    {
        var query = _context.Stocks.AsQueryable();
        
        // 市场筛选
        if (!string.IsNullOrEmpty(criteria.Market))
        {
            query = query.Where(s => s.Market == criteria.Market);
        }
        
        // 价格条件
        if (criteria.MinPrice.HasValue)
        {
            query = query.Where(s => s.CurrentPrice >= criteria.MinPrice.Value);
        }
        if (criteria.MaxPrice.HasValue)
        {
            query = query.Where(s => s.CurrentPrice <= criteria.MaxPrice.Value);
        }
        
        // 涨跌幅条件
        if (criteria.MinChangePercent.HasValue)
        {
            query = query.Where(s => s.ChangePercent >= criteria.MinChangePercent.Value);
        }
        if (criteria.MaxChangePercent.HasValue)
        {
            query = query.Where(s => s.ChangePercent <= criteria.MaxChangePercent.Value);
        }
        
        // 换手率条件
        if (criteria.MinTurnoverRate.HasValue)
        {
            query = query.Where(s => s.TurnoverRate >= criteria.MinTurnoverRate.Value);
        }
        if (criteria.MaxTurnoverRate.HasValue)
        {
            query = query.Where(s => s.TurnoverRate <= criteria.MaxTurnoverRate.Value);
        }
        
        // PE条件
        if (criteria.MinPE.HasValue)
        {
            query = query.Where(s => s.PE >= criteria.MinPE.Value && s.PE > 0);
        }
        if (criteria.MaxPE.HasValue)
        {
            query = query.Where(s => s.PE <= criteria.MaxPE.Value && s.PE > 0);
        }
        
        // PB条件
        if (criteria.MinPB.HasValue)
        {
            query = query.Where(s => s.PB >= criteria.MinPB.Value && s.PB > 0);
        }
        if (criteria.MaxPB.HasValue)
        {
            query = query.Where(s => s.PB <= criteria.MaxPB.Value && s.PB > 0);
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
        
        var results = await query.ToListAsync();
        
        _logger.LogInformation("条件选股查询返回 {Count} 条结果", results.Count);
        
        return results;
    }
}

