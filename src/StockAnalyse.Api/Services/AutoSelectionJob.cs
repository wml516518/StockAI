using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace StockAnalyse.Api.Services;

/// <summary>
/// 自动选股后台服务
/// 定时执行选股任务，筛选符合条件的股票并自动加入自选股
/// </summary>
public class AutoSelectionJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoSelectionJob> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1); // 每分钟检查一次

    // 配置：执行时间（每个工作日的9:15和14:30）
    private readonly TimeSpan _morningExecutionTime = new TimeSpan(9, 15, 0);
    private readonly TimeSpan _afternoonExecutionTime = new TimeSpan(14, 30, 0);
    private DateTime? _lastMorningExecution = null;
    private DateTime? _lastAfternoonExecution = null;


    public AutoSelectionJob(
        IServiceProvider serviceProvider,
        ILogger<AutoSelectionJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("自动选股后台服务已启动，检查间隔: {Interval} 分钟", _checkInterval.TotalMinutes);

        // 首次延迟1分钟后再开始，避免服务启动时立即执行
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var currentTime = now.TimeOfDay;
                var currentDayOfWeek = now.DayOfWeek;

                // 只在工作日执行（周一到周五）
                if (currentDayOfWeek != DayOfWeek.Saturday && currentDayOfWeek != DayOfWeek.Sunday)
                {
                    // 检查是否到了早上9:15的执行时间
                    if (IsTimeToExecute(currentTime, _morningExecutionTime, ref _lastMorningExecution, now))
                    {
                        _logger.LogInformation("检测到早上执行时间（9:15），开始执行自动选股任务...");
                        await ExecuteSelectionWithSaveAsync(stoppingToken);
                    }
                    // 检查是否到了下午14:30的执行时间
                    else if (IsTimeToExecute(currentTime, _afternoonExecutionTime, ref _lastAfternoonExecution, now))
                    {
                        _logger.LogInformation("检测到下午执行时间（14:30），开始执行自动选股任务...");
                        await ExecuteSelectionWithSaveAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行自动选股任务时发生错误");
            }

            // 等待指定时间后再次检查
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("自动选股后台服务已停止");
    }

    /// <summary>
    /// 判断是否到了执行时间
    /// </summary>
    private bool IsTimeToExecute(TimeSpan currentTime, TimeSpan targetTime, ref DateTime? lastExecution, DateTime now)
    {
        // 检查当前时间是否在目标时间的1分钟窗口内
        var timeDiff = Math.Abs((currentTime - targetTime).TotalMinutes);
        
        if (timeDiff <= 1.0) // 在目标时间的1分钟内
        {
            // 检查今天是否已经执行过
            if (lastExecution == null || lastExecution.Value.Date != now.Date)
            {
                lastExecution = now;
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// 执行选股任务并保存到自选股（用于后台定时任务）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task ExecuteSelectionWithSaveAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var autoSelectionService = scope.ServiceProvider.GetRequiredService<IAutoSelectionService>();
        var watchlistService = scope.ServiceProvider.GetRequiredService<IWatchlistService>();

        try
        {
            _logger.LogInformation("开始执行自动选股流程（后台任务）...");

            // 使用AutoSelectionService执行选股
            var result = await autoSelectionService.ExecuteSelectionAsync(cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("自动选股执行失败: {ErrorMessage}", result.ErrorMessage);
                return;
            }

            if (result.SelectedStocks == null || result.SelectedStocks.Count == 0)
            {
                _logger.LogInformation("自动选股未找到符合条件的股票");
                return;
            }

            // 获取或创建"自动选股"分类
            var categories = await watchlistService.GetCategoriesAsync();
            var autoSelectionCategory = categories.FirstOrDefault(c => 
                c.Name.Equals("自动选股", StringComparison.OrdinalIgnoreCase));

            if (autoSelectionCategory == null)
            {
                autoSelectionCategory = await watchlistService.CreateCategoryAsync(
                    "自动选股",
                    "系统自动选股任务筛选出的股票",
                    "#ff9800");
                _logger.LogInformation("创建了新的自选股分类: 自动选股");
            }

            // 保存到自选股
            int successCount = 0;
            int skipCount = 0;
            foreach (var selectedStock in result.SelectedStocks)
            {
                try
                {
                    // 检查是否已存在
                    var existingWatchlist = await watchlistService.GetWatchlistByCategoryAsync(autoSelectionCategory.Id);
                    if (existingWatchlist.Any(w => w.StockCode.Equals(selectedStock.StockCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        skipCount++;
                        _logger.LogDebug("股票 {StockCode} 已存在于自选股，跳过", selectedStock.StockCode);
                        continue;
                    }

                    await watchlistService.AddToWatchlistAsync(selectedStock.StockCode, autoSelectionCategory.Id);
                    successCount++;
                    _logger.LogDebug("成功将股票 {StockCode} (评分: {Score}) 添加到自选股", selectedStock.StockCode, selectedStock.AIScore);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "添加股票 {StockCode} 到自选股失败", selectedStock.StockCode);
                }
            }

            _logger.LogInformation("自动选股任务完成 - 成功添加: {SuccessCount}, 跳过: {SkipCount}, 总计: {TotalCount}",
                successCount, skipCount, result.SelectedStocks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行自动选股任务时发生错误");
        }
    }
}

