using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class TradingPlanBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TradingPlanBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1); // 每1分钟检查一次，确保能及时响应不同的更新间隔设置

    public TradingPlanBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<TradingPlanBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("做T方案后台服务已启动，检查间隔: {Interval} 分钟", _checkInterval.TotalMinutes);

        // 首次延迟1分钟后再开始，避免服务启动时立即执行大量更新
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var tradingPlanService = scope.ServiceProvider.GetRequiredService<ITradingPlanService>();

                await tradingPlanService.UpdateAllTradingPlansAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新做T方案时发生错误");
            }

            // 等待指定时间后再次执行
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("做T方案后台服务已停止");
    }
}

