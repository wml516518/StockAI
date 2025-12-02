using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class TradingPlanBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TradingPlanBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // 每5分钟检查一次
    
    // 配置：交易时间段（工作日9:15-14:30）
    private readonly TimeSpan _tradingStartTime = new TimeSpan(9, 15, 0);  // 9:15开始
    private readonly TimeSpan _tradingEndTime = new TimeSpan(14, 30, 0);   // 14:30结束

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
        _logger.LogInformation("交易时间段: 工作日 {StartTime:hh\\:mm} - {EndTime:hh\\:mm}", _tradingStartTime, _tradingEndTime);

        // 首次延迟1分钟后再开始，避免服务启动时立即执行大量更新
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var currentTime = now.TimeOfDay;
                var currentDayOfWeek = now.DayOfWeek;

                // 检查是否在交易时间段内
                if (IsWithinTradingHours(currentTime, currentDayOfWeek))
                {
                    using var scope = _serviceProvider.CreateScope();
                    var tradingPlanService = scope.ServiceProvider.GetRequiredService<ITradingPlanService>();

                    _logger.LogDebug("在交易时间段内，开始更新做T方案...");
                    await tradingPlanService.UpdateAllTradingPlansAsync();
                }
                else
                {
                    // 不在交易时间段，跳过执行
                    var reason = GetSkipReason(currentTime, currentDayOfWeek);
                    _logger.LogDebug("不在交易时间段，跳过更新做T方案: {Reason}", reason);
                }
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

    /// <summary>
    /// 检查是否在交易时间段内（工作日9:15-14:30）
    /// </summary>
    private bool IsWithinTradingHours(TimeSpan currentTime, DayOfWeek dayOfWeek)
    {
        // 只在工作日执行（周一到周五）
        if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
        {
            return false;
        }

        // 检查是否在9:15到14:30之间
        return currentTime >= _tradingStartTime && currentTime <= _tradingEndTime;
    }

    /// <summary>
    /// 获取跳过执行的原因（用于日志）
    /// </summary>
    private string GetSkipReason(TimeSpan currentTime, DayOfWeek dayOfWeek)
    {
        if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
        {
            return $"非工作日 ({dayOfWeek})";
        }

        if (currentTime < _tradingStartTime)
        {
            return $"未到交易开始时间 (当前: {currentTime:hh\\:mm}, 开始: {_tradingStartTime:hh\\:mm})";
        }

        if (currentTime > _tradingEndTime)
        {
            return $"已过交易结束时间 (当前: {currentTime:hh\\:mm}, 结束: {_tradingEndTime:hh\\:mm})";
        }

        return "未知原因";
    }
}

