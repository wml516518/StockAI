using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class PriceAlertService : IPriceAlertService
{
    private readonly StockDbContext _context;
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<PriceAlertService> _logger;

    public PriceAlertService(
        StockDbContext context,
        IStockDataService stockDataService,
        ILogger<PriceAlertService> logger)
    {
        _context = context;
        _stockDataService = stockDataService;
        _logger = logger;
    }

    public async Task CheckSuggestedPriceAlertsAsync()
    {
        // 获取所有设置了建议价格且未发送提醒的自选股
        var watchlistStocks = await _context.WatchlistStocks
            .Include(w => w.Stock)
            .Include(w => w.Category)
            .Where(w => 
                (w.SuggestedBuyPrice.HasValue && !w.BuyAlertSent) ||
                (w.SuggestedSellPrice.HasValue && !w.SellAlertSent)
            )
            .ToListAsync();
        
        if (watchlistStocks.Count == 0)
        {
            return;
        }
        
        _logger.LogDebug("检查 {Count} 个自选股的建议价格提醒", watchlistStocks.Count);
        
        foreach (var watchlistStock in watchlistStocks)
        {
            try
            {
                // 获取实时行情
                var stock = await _stockDataService.GetWatchlistRealTimeQuoteAsync(watchlistStock.StockCode);
                
                if (stock == null)
                {
                    _logger.LogDebug("无法获取股票 {StockCode} 的实时行情，跳过提醒检查", watchlistStock.StockCode);
                    continue;
                }
                
                // 检查买入价提醒
                if (watchlistStock.SuggestedBuyPrice.HasValue && 
                    !watchlistStock.BuyAlertSent && 
                    stock.CurrentPrice <= watchlistStock.SuggestedBuyPrice.Value)
                {
                    watchlistStock.BuyAlertSent = true;
                    watchlistStock.LastUpdate = DateTime.Now;
                    
                    var buyMessage = $"🟢 买入提醒: {stock.Name}({watchlistStock.StockCode}) 当前价格 {stock.CurrentPrice:F2} 已达到建议买入价 {watchlistStock.SuggestedBuyPrice.Value:F2}";
                    
                    _logger.LogWarning("买入提醒触发: {Message}", buyMessage);
                    await SendNotificationAsync(buyMessage);
                }
                
                // 检查卖出价提醒
                if (watchlistStock.SuggestedSellPrice.HasValue && 
                    !watchlistStock.SellAlertSent && 
                    stock.CurrentPrice >= watchlistStock.SuggestedSellPrice.Value)
                {
                    watchlistStock.SellAlertSent = true;
                    watchlistStock.LastUpdate = DateTime.Now;
                    
                    var sellMessage = $"🔴 卖出提醒: {stock.Name}({watchlistStock.StockCode}) 当前价格 {stock.CurrentPrice:F2} 已达到建议卖出价 {watchlistStock.SuggestedSellPrice.Value:F2}";
                    
                    _logger.LogWarning("卖出提醒触发: {Message}", sellMessage);
                    await SendNotificationAsync(sellMessage);
                }
                
                // 保存更改
                if (watchlistStock.BuyAlertSent || watchlistStock.SellAlertSent)
                {
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查自选股建议价格提醒失败: {StockCode}", watchlistStock.StockCode);
            }
        }
    }

    private async Task SendNotificationAsync(string message)
    {
        // 这里可以实现各种通知方式
        // 例如：邮件、短信、微信推送、桌面通知等
        
        _logger.LogInformation("发送通知: {Message}", message);
        
        await Task.CompletedTask;
    }
}

