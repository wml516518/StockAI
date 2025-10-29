using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class WatchlistService : IWatchlistService
{
    private readonly StockDbContext _context;
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<WatchlistService> _logger;

    public WatchlistService(
        StockDbContext context,
        IStockDataService stockDataService,
        ILogger<WatchlistService> logger)
    {
        _context = context;
        _stockDataService = stockDataService;
        _logger = logger;
    }

    public async Task<WatchlistStock> AddToWatchlistAsync(string stockCode, int categoryId, decimal? costPrice = null, decimal? quantity = null)
    {
        // 检查是否已存在
        var existing = await _context.WatchlistStocks
            .FirstOrDefaultAsync(w => w.StockCode == stockCode && w.WatchlistCategoryId == categoryId);
            
        if (existing != null)
        {
            throw new InvalidOperationException("该股票已存在于此分类");
        }
        
        // 获取股票数据
        var stock = await _stockDataService.GetRealTimeQuoteAsync(stockCode);
        
        // 如果API获取失败，尝试从数据库获取
        if (stock == null)
        {
            stock = await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stockCode);
        }
        
        // 如果仍然没有数据，创建基本股票记录
        if (stock == null)
        {
            stock = new Stock
            {
                Code = stockCode,
                Name = "未知",
                Market = stockCode.StartsWith("6") ? "SH" : "SZ",
                LastUpdate = DateTime.Now
            };
            await _context.Stocks.AddAsync(stock);
            await _context.SaveChangesAsync();
        }
        
        var watchlistStock = new WatchlistStock
        {
            StockCode = stockCode,
            WatchlistCategoryId = categoryId,
            CostPrice = costPrice,
            Quantity = quantity,
            TotalCost = (costPrice ?? 0) * (quantity ?? 0),
            AddTime = DateTime.Now,
            LastUpdate = DateTime.Now
        };
        
        await _context.WatchlistStocks.AddAsync(watchlistStock);
        await _context.SaveChangesAsync();
        
        // 计算盈亏
        watchlistStock = await CalculateProfitLossAsync(watchlistStock.Id);
        
        return watchlistStock;
    }

    public async Task<bool> RemoveFromWatchlistAsync(int watchlistStockId)
    {
        var item = await _context.WatchlistStocks.FindAsync(watchlistStockId);
        if (item == null)
        {
            return false;
        }
        
        _context.WatchlistStocks.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<WatchlistStock> UpdateCostInfoAsync(int id, decimal? costPrice, decimal? quantity)
    {
        var item = await _context.WatchlistStocks
            .Include(w => w.Stock)
            .FirstOrDefaultAsync(w => w.Id == id);
            
        if (item == null)
        {
            throw new KeyNotFoundException("自选股不存在");
        }
        
        item.CostPrice = costPrice;
        item.Quantity = quantity;
        item.TotalCost = (costPrice ?? 0) * (quantity ?? 0);
        item.LastUpdate = DateTime.Now;
        
        await _context.SaveChangesAsync();
        
        return await CalculateProfitLossAsync(id);
    }

    public async Task<WatchlistStock> UpdatePriceAlertAsync(int id, decimal? highAlert, decimal? lowAlert)
    {
        var item = await _context.WatchlistStocks.FindAsync(id);
        if (item == null)
        {
            throw new KeyNotFoundException("自选股不存在");
        }
        
        item.HighAlertPrice = highAlert;
        item.LowAlertPrice = lowAlert;
        item.HighAlertSent = false;
        item.LowAlertSent = false;
        item.LastUpdate = DateTime.Now;
        
        await _context.SaveChangesAsync();
        return item;
    }

    public async Task<Dictionary<string, List<WatchlistStock>>> GetWatchlistGroupedByCategoryAsync()
    {
        var items = await _context.WatchlistStocks
            .Include(w => w.Category)
            .ToListAsync();
            
        // 为每个自选股获取实时行情数据，而不是使用数据库中的缓存数据
        foreach (var item in items)
        {
            var realTimeStock = await _stockDataService.GetWatchlistRealTimeQuoteAsync(item.StockCode);
            if (realTimeStock != null)
            {
                item.Stock = realTimeStock;
            }
        }
            
        return items.GroupBy(w => w.Category?.Name ?? "未分类")
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<List<WatchlistStock>> GetWatchlistByCategoryAsync(int categoryId)
    {
        return await _context.WatchlistStocks
            .Include(w => w.Stock)
            .Where(w => w.WatchlistCategoryId == categoryId)
            .ToListAsync();
    }

    public async Task<WatchlistStock> CalculateProfitLossAsync(int id)
    {
        var item = await _context.WatchlistStocks
            .Include(w => w.Stock)
            .FirstOrDefaultAsync(w => w.Id == id);
            
        if (item?.Stock == null)
        {
            throw new KeyNotFoundException("自选股不存在");
        }
        
        // 获取最新价格（使用专门的自选股实时数据方法）
        var stock = await _stockDataService.GetWatchlistRealTimeQuoteAsync(item.StockCode);
        
        if (stock != null && item.CostPrice.HasValue && item.Quantity.HasValue && item.CostPrice.Value > 0)
        {
            var currentValue = stock.CurrentPrice * item.Quantity.Value;
            item.ProfitLoss = currentValue - item.TotalCost;
            item.ProfitLossPercent = item.TotalCost > 0 ? item.ProfitLoss / item.TotalCost * 100 : 0;
        }
        
        item.LastUpdate = DateTime.Now;
        await _context.SaveChangesAsync();
        
        return item;
    }

    public async Task<List<WatchlistCategory>> GetCategoriesAsync()
    {
        return await _context.WatchlistCategories
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<WatchlistCategory> CreateCategoryAsync(string name, string? description = null, string color = "#1890ff")
    {
        var category = new WatchlistCategory
        {
            Name = name,
            Description = description,
            Color = color
        };
        
        await _context.WatchlistCategories.AddAsync(category);
        await _context.SaveChangesAsync();
        
        return category;
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        var category = await _context.WatchlistCategories
            .Include(c => c.Stocks)
            .FirstOrDefaultAsync(c => c.Id == id);
            
        if (category == null)
        {
            return false;
        }
        
        // 如果分类下有股票，不允许删除
        if (category.Stocks.Any())
        {
            throw new InvalidOperationException("该分类下还有股票，无法删除");
        }
        
        _context.WatchlistCategories.Remove(category);
        await _context.SaveChangesAsync();
        return true;
    }
}

