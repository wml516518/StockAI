using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class TradingPlanBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TradingPlanBackgroundService> _logger;
    private readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(10); // 每10分钟检查一次

    public TradingPlanBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<TradingPlanBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("做T方案后台服务已启动，更新间隔: {Interval} 分钟", _updateInterval.TotalMinutes);

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
            await Task.Delay(_updateInterval, stoppingToken);
        }

        _logger.LogInformation("做T方案后台服务已停止");
    }
}

