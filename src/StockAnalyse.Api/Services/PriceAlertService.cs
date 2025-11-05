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

    public async Task<PriceAlert> CreateAlertAsync(string stockCode, decimal targetPrice, AlertType type)
    {
        var alert = new PriceAlert
        {
            StockCode = stockCode,
            TargetPrice = targetPrice,
            Type = type,
            IsTriggered = false,
            CreateTime = DateTime.Now
        };
        
        await _context.PriceAlerts.AddAsync(alert);
        await _context.SaveChangesAsync();
        
        return alert;
    }

    public async Task<List<PriceAlert>> GetActiveAlertsAsync()
    {
        return await _context.PriceAlerts
            .Where(a => !a.IsTriggered)
            .ToListAsync();
    }

    public async Task CheckAndTriggerAlertsAsync()
    {
        var activeAlerts = await GetActiveAlertsAsync();
        
        // 如果没有活跃提醒，直接返回，避免不必要的API调用
        if (activeAlerts.Count == 0)
        {
            return;
        }
        
        _logger.LogDebug("检查 {Count} 个价格提醒", activeAlerts.Count);
        
        foreach (var alert in activeAlerts)
        {
            try
            {
                // 使用GetWatchlistRealTimeQuoteAsync，避免保存到数据库，减少数据库操作
                var stock = await _stockDataService.GetWatchlistRealTimeQuoteAsync(alert.StockCode);
                
                if (stock == null)
                {
                    _logger.LogDebug("无法获取股票 {StockCode} 的实时行情，跳过提醒检查", alert.StockCode);
                    continue;
                }
                
                bool shouldTrigger = false;
                string message = string.Empty;
                
                switch (alert.Type)
                {
                    case AlertType.PriceUp:
                        if (stock.CurrentPrice >= alert.TargetPrice)
                        {
                            shouldTrigger = true;
                            message = $"{stock.Name}({alert.StockCode}) 价格上涨至 {stock.CurrentPrice}，已超过目标价格 {alert.TargetPrice}";
                        }
                        break;
                        
                    case AlertType.PriceDown:
                        if (stock.CurrentPrice <= alert.TargetPrice)
                        {
                            shouldTrigger = true;
                            message = $"{stock.Name}({alert.StockCode}) 价格下跌至 {stock.CurrentPrice}，已低于目标价格 {alert.TargetPrice}";
                        }
                        break;
                        
                    case AlertType.PriceReach:
                        if (Math.Abs(stock.CurrentPrice - alert.TargetPrice) < 0.01m)
                        {
                            shouldTrigger = true;
                            message = $"{stock.Name}({alert.StockCode}) 价格到达目标价格 {alert.TargetPrice}";
                        }
                        break;
                }
                
                if (shouldTrigger)
                {
                    alert.IsTriggered = true;
                    alert.TriggerTime = DateTime.Now;
                    
                    await _context.SaveChangesAsync();
                    
                    _logger.LogWarning("价格提醒触发: {Message}", message);
                    
                    // 这里可以发送通知（邮件、短信、推送等）
                    await SendNotificationAsync(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查价格提醒失败: {AlertId}", alert.Id);
            }
        }
    }

    public async Task<bool> DeleteAlertAsync(int id)
    {
        var alert = await _context.PriceAlerts.FindAsync(id);
        if (alert == null)
        {
            return false;
        }
        
        _context.PriceAlerts.Remove(alert);
        await _context.SaveChangesAsync();
        return true;
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

