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
        
        foreach (var alert in activeAlerts)
        {
            try
            {
                var stock = await _stockDataService.GetRealTimeQuoteAsync(alert.StockCode);
                
                if (stock == null)
                    continue;
                
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

    private async Task SendNotificationAsync(string message)
    {
        // 这里可以实现各种通知方式
        // 例如：邮件、短信、微信推送、桌面通知等
        
        _logger.LogInformation("发送通知: {Message}", message);
        
        await Task.CompletedTask;
    }
}

