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
        
        // 重要：先检查并分离可能被跟踪的 Stock 实体，避免跟踪冲突
        // 这是因为 StockDataService 和 WatchlistService 共享同一个 DbContext
        var potentiallyTrackedStock = await _context.Stocks.FindAsync(stockCode);
        if (potentiallyTrackedStock != null)
        {
            var entry = _context.Entry(potentiallyTrackedStock);
            if (entry.State != EntityState.Detached)
            {
                // 分离已跟踪的实体，让 StockDataService 可以重新查询和跟踪
                entry.State = EntityState.Detached;
            }
        }
        
        // 调用 API 获取股票数据（会自动保存到数据库）
        // StockDataService.SaveStockDataAsync 会查询并跟踪 Stock，所以我们需要先分离
        await _stockDataService.GetRealTimeQuoteAsync(stockCode);
        
        // 重新从数据库查询 Stock（StockDataService 已经保存并跟踪了它）
        // 使用 Find 会优先返回已跟踪的实体
        var dbStock = await _context.Stocks.FindAsync(stockCode);
        
        // 如果 Find 返回 null（理论上不应该），使用 FirstOrDefault 查询
        if (dbStock == null)
        {
            dbStock = await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stockCode);
        }
        
        // 如果数据库中仍然不存在，创建基本股票记录
        if (dbStock == null)
        {
            dbStock = new Stock
            {
                Code = stockCode,
                Name = "未知",
                Market = stockCode.StartsWith("6") ? "SH" : "SZ",
                LastUpdate = DateTime.Now
            };
            await _context.Stocks.AddAsync(dbStock);
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
        
        // 重新加载以包含导航属性（使用 Include 加载 Stock，确保使用被跟踪的实体）
        watchlistStock = await _context.WatchlistStocks
            .Include(w => w.Category)
            .Include(w => w.Stock)
            .FirstOrDefaultAsync(w => w.Id == watchlistStock.Id);
        
        // 计算盈亏
        watchlistStock = await CalculateProfitLossAsync(watchlistStock.Id);
        
        return watchlistStock;
    }

    public async Task<bool> RemoveFromWatchlistAsync(int watchlistStockId)
    {
        try
        {
            // 先查询要删除的自选股，获取 StockCode
            var watchlistItem = await _context.WatchlistStocks
                .AsNoTracking()
                .Where(w => w.Id == watchlistStockId)
                .Select(w => new { w.Id, w.StockCode })
                .FirstOrDefaultAsync();
            
            if (watchlistItem == null)
            {
                _logger.LogWarning("删除自选股失败：未找到 ID 为 {Id} 的自选股", watchlistStockId);
                return false;
            }
            
            var stockCode = watchlistItem.StockCode;
            
            // 删除 WatchlistStock
            var deleted = await _context.WatchlistStocks
                .Where(w => w.Id == watchlistStockId)
                .ExecuteDeleteAsync();
            
            if (deleted == 0)
            {
                _logger.LogWarning("删除自选股失败：ExecuteDelete 返回 0，ID: {Id}", watchlistStockId);
                return false;
            }
            
            _logger.LogInformation("成功删除自选股，ID: {Id}，股票代码: {StockCode}", watchlistStockId, stockCode);
            
            // 检查该股票是否还有其他自选股记录
            var hasOtherWatchlist = await _context.WatchlistStocks
                .AnyAsync(w => w.StockCode == stockCode);
            
            if (!hasOtherWatchlist)
            {
                // 没有其他自选股记录，检查是否有历史数据
                var hasHistory = await _context.StockHistories
                    .AnyAsync(h => h.StockCode == stockCode);
                
                if (!hasHistory)
                {
                    // 既没有其他自选股，也没有历史数据，删除 Stock
                    // 先检查并分离可能被跟踪的 Stock 实体，避免跟踪冲突
                    var trackedStock = _context.ChangeTracker.Entries<Stock>()
                        .FirstOrDefault(e => e.Entity.Code == stockCode);
                    
                    if (trackedStock != null && trackedStock.State != EntityState.Detached)
                    {
                        trackedStock.State = EntityState.Detached;
                    }
                    
                    // 使用 ExecuteDeleteAsync 直接执行 SQL，不加载实体，避免跟踪冲突
                    var stockDeleted = await _context.Stocks
                        .Where(s => s.Code == stockCode)
                        .ExecuteDeleteAsync();
                    
                    if (stockDeleted > 0)
                    {
                        _logger.LogInformation("成功删除股票记录，股票代码: {StockCode}（已无自选股和历史数据）", stockCode);
                    }
                }
                else
                {
                    _logger.LogInformation("保留股票记录，股票代码: {StockCode}（存在历史数据）", stockCode);
                }
            }
            else
            {
                _logger.LogInformation("保留股票记录，股票代码: {StockCode}（仍有其他自选股记录）", stockCode);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除自选股时发生异常，ID: {Id}，错误: {Message}", watchlistStockId, ex.Message);
            throw;
        }
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
        // 使用 AsNoTracking 避免跟踪实体，防止后续设置 Stock 时冲突
        var items = await _context.WatchlistStocks
            .Include(w => w.Category)
            .AsNoTracking()
            .ToListAsync();
            
        // 批量获取所有股票代码的实时行情（并行处理，提高性能）
        var stockCodes = items.Select(w => w.StockCode).Distinct().ToList();
        var realTimeStocks = await _stockDataService.GetBatchQuotesAsync(stockCodes);
        
        // 创建 Stock 字典，确保 Stock 对象未被跟踪
        // GetBatchQuotesAsync 返回的 Stock 是新创建的对象，不应该被跟踪
        // 但为了安全起见，我们仍然检查并分离可能被跟踪的实体
        var stockDict = new Dictionary<string, Stock>();
        foreach (var stock in realTimeStocks)
        {
            if (stock != null)
            {
                // 检查 Stock 是否已被跟踪，如果已跟踪则分离
                // 使用 ChangeTracker 来检查实体是否被跟踪
                var trackedEntity = _context.ChangeTracker.Entries<Stock>()
                    .FirstOrDefault(e => e.Entity.Code == stock.Code);
                
                if (trackedEntity != null && trackedEntity.State != EntityState.Detached)
                {
                    trackedEntity.State = EntityState.Detached;
                }
                
                stockDict[stock.Code] = stock;
            }
        }
        
        // 为每个自选股设置实时行情数据
        foreach (var item in items)
        {
            if (stockDict.TryGetValue(item.StockCode, out var realTimeStock))
            {
                // 直接设置 Stock 对象
                // 因为使用了 AsNoTracking，WatchlistStock 未被跟踪
                // Stock 对象也是新创建的，未被跟踪
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
            .Include(w => w.Category)
            .FirstOrDefaultAsync(w => w.Id == id);
            
        if (item == null)
        {
            throw new KeyNotFoundException("自选股不存在");
        }
        
        // 获取最新价格（使用专门的自选股实时数据方法）
        var stock = await _stockDataService.GetWatchlistRealTimeQuoteAsync(item.StockCode);
        
        // 更新Stock数据
        if (stock != null)
        {
            // 检查并分离可能被跟踪的 Stock 实体，避免冲突
            // 先分离 item.Stock（如果存在且被跟踪）
            if (item.Stock != null)
            {
                var trackedStock = _context.ChangeTracker.Entries<Stock>()
                    .FirstOrDefault(e => e.Entity.Code == item.Stock.Code);
                
                if (trackedStock != null && trackedStock.State != EntityState.Detached)
                {
                    trackedStock.State = EntityState.Detached;
                }
            }
            
            // 分离新获取的 stock（如果被跟踪）
            var trackedNewStock = _context.ChangeTracker.Entries<Stock>()
                .FirstOrDefault(e => e.Entity.Code == stock.Code);
            
            if (trackedNewStock != null && trackedNewStock.State != EntityState.Detached)
            {
                trackedNewStock.State = EntityState.Detached;
            }
            
            item.Stock = stock;
        }
        
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

    public async Task<WatchlistStock> UpdateCategoryAsync(int id, int categoryId)
    {
        var item = await _context.WatchlistStocks
            .Include(w => w.Category)
            .Include(w => w.Stock)
            .FirstOrDefaultAsync(w => w.Id == id);
            
        if (item == null)
        {
            throw new KeyNotFoundException("自选股不存在");
        }
        
        // 检查目标分类是否存在
        var category = await _context.WatchlistCategories.FindAsync(categoryId);
        if (category == null)
        {
            throw new KeyNotFoundException("分类不存在");
        }
        
        // 检查该股票是否已在目标分类中存在
        var existing = await _context.WatchlistStocks
            .FirstOrDefaultAsync(w => w.StockCode == item.StockCode && w.WatchlistCategoryId == categoryId && w.Id != id);
            
        if (existing != null)
        {
            throw new InvalidOperationException("该股票已存在于目标分类中");
        }
        
        item.WatchlistCategoryId = categoryId;
        item.LastUpdate = DateTime.Now;
        
        await _context.SaveChangesAsync();
        
        // 重新加载以获取更新后的Category
        item = await _context.WatchlistStocks
            .Include(w => w.Category)
            .Include(w => w.Stock)
            .FirstOrDefaultAsync(w => w.Id == id);
        
        // 如果Stock数据不存在，获取实时行情
        if (item != null && item.Stock == null)
        {
            var stock = await _stockDataService.GetWatchlistRealTimeQuoteAsync(item.StockCode);
            if (stock != null)
            {
                // 检查并分离可能被跟踪的 Stock 实体，避免冲突
                var trackedStock = _context.ChangeTracker.Entries<Stock>()
                    .FirstOrDefault(e => e.Entity.Code == stock.Code);
                
                if (trackedStock != null && trackedStock.State != EntityState.Detached)
                {
                    trackedStock.State = EntityState.Detached;
                }
                
                // 如果 item.Stock 已被跟踪，也分离它
                if (item.Stock != null)
                {
                    var trackedItemStock = _context.ChangeTracker.Entries<Stock>()
                        .FirstOrDefault(e => e.Entity.Code == item.Stock.Code);
                    
                    if (trackedItemStock != null && trackedItemStock.State != EntityState.Detached)
                    {
                        trackedItemStock.State = EntityState.Detached;
                    }
                }
                
                item.Stock = stock;
            }
        }
        
        return item;
    }

    public async Task<WatchlistStock> UpdateSuggestedPriceAsync(int id, decimal? suggestedBuyPrice, decimal? suggestedSellPrice)
    {
        var item = await _context.WatchlistStocks
            .Include(w => w.Category)
            .FirstOrDefaultAsync(w => w.Id == id);
            
        if (item == null)
        {
            throw new KeyNotFoundException("自选股不存在");
        }
        
        // 保存旧值用于比较
        var oldBuyPrice = item.SuggestedBuyPrice;
        var oldSellPrice = item.SuggestedSellPrice;
        
        item.SuggestedBuyPrice = suggestedBuyPrice;
        item.SuggestedSellPrice = suggestedSellPrice;
        
        // 如果更新了建议价格，重置提醒标志，允许重新提醒
        if (suggestedBuyPrice.HasValue && oldBuyPrice != suggestedBuyPrice)
        {
            item.BuyAlertSent = false;
        }
        if (suggestedSellPrice.HasValue && oldSellPrice != suggestedSellPrice)
        {
            item.SellAlertSent = false;
        }
        
        item.LastUpdate = DateTime.Now;
        
        await _context.SaveChangesAsync();
        
        return item;
    }

    public async Task<WatchlistStock> ResetAlertFlagsAsync(int id, decimal currentPrice)
    {
        var item = await _context.WatchlistStocks
            .Include(w => w.Category)
            .FirstOrDefaultAsync(w => w.Id == id);
            
        if (item == null)
        {
            throw new KeyNotFoundException("自选股不存在");
        }
        
        bool needSave = false;
        
        // 如果当前价格高于建议买入价，重置买入提醒标志
        if (item.SuggestedBuyPrice.HasValue && 
            item.BuyAlertSent && 
            currentPrice > item.SuggestedBuyPrice.Value)
        {
            item.BuyAlertSent = false;
            needSave = true;
        }
        
        // 如果当前价格低于建议卖出价，重置卖出提醒标志
        if (item.SuggestedSellPrice.HasValue && 
            item.SellAlertSent && 
            currentPrice < item.SuggestedSellPrice.Value)
        {
            item.SellAlertSent = false;
            needSave = true;
        }
        
        if (needSave)
        {
            item.LastUpdate = DateTime.Now;
            await _context.SaveChangesAsync();
        }
        
        return item;
    }
}


