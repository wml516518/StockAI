using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SimpleBacktestController : ControllerBase
{
    private readonly StockDbContext _context;
    private readonly IBacktestService _backtestService;
    private readonly IQuantTradingService _quantTradingService;
    private readonly ILogger<SimpleBacktestController> _logger;

    public SimpleBacktestController(
        StockDbContext context,
        IBacktestService backtestService,
        IQuantTradingService quantTradingService,
        ILogger<SimpleBacktestController> logger)
    {
        _context = context;
        _backtestService = backtestService;
        _quantTradingService = quantTradingService;
        _logger = logger;
    }

    /// <summary>
    /// 一键回测 - 使用默认的简单移动平均策略
    /// </summary>
    [HttpPost("quick-test")]
    public async Task<IActionResult> QuickBacktest([FromBody] QuickBacktestRequest request)
    {
        try
        {
            // 验证输入
            if (string.IsNullOrWhiteSpace(request.StockCode))
                return BadRequest("请输入股票代码");

            if (request.StartDate >= request.EndDate)
                return BadRequest("开始日期必须早于结束日期");

            // 查找或创建简单移动平均策略
            var strategy = await GetOrCreateSimpleStrategy();
            
            // 执行回测
            var result = await _backtestService.RunBacktestAsync(
                strategy.Id, 
                request.StartDate, 
                request.EndDate, 
                request.InitialCapital,
                new List<string> { request.StockCode }
            );

            return Ok(new
            {
                stockCode = request.StockCode,
                strategyName = strategy.Name,
                totalReturn = result.TotalReturn,
                annualizedReturn = result.AnnualizedReturn,
                maxDrawdown = result.MaxDrawdown,
                sharpeRatio = result.SharpeRatio,
                totalTrades = result.TotalTrades,
                winRate = result.WinRate,
                message = $"回测完成！使用策略：{strategy.Name}，总收益率：{(result.TotalReturn * 100):F2}%"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "一键回测失败");
            return BadRequest($"回测失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 批量一键回测 - 对多只股票使用简单策略
    /// </summary>
    [HttpPost("quick-batch-test")]
    public async Task<IActionResult> QuickBatchBacktest([FromBody] QuickBatchBacktestRequest request)
    {
        try
        {
            // 验证输入
            if (request.StockCodes == null || !request.StockCodes.Any())
                return BadRequest("请至少输入一个股票代码");

            if (request.StartDate >= request.EndDate)
                return BadRequest("开始日期必须早于结束日期");

            // 查找或创建简单移动平均策略
            var strategy = await GetOrCreateSimpleStrategy();
            
            // 执行批量回测
            var results = await _backtestService.RunBatchBacktestAsync(
                strategy.Id, 
                request.StartDate, 
                request.EndDate, 
                request.InitialCapital,
                request.StockCodes.Distinct().ToList()
            );

            return Ok(new
            {
                strategyName = strategy.Name,
                results = results,
                summary = new
                {
                    totalStocks = results.Count,
                    profitableStocks = results.Count(r => r.TotalReturn > 0),
                    averageReturn = results.Average(r => r.TotalReturn),
                    bestPerformer = results.OrderByDescending(r => r.TotalReturn).FirstOrDefault()?.StockCode,
                    worstPerformer = results.OrderBy(r => r.TotalReturn).FirstOrDefault()?.StockCode
                },
                message = $"批量回测完成！共测试 {results.Count} 只股票，盈利股票 {results.Count(r => r.TotalReturn > 0)} 只"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量一键回测失败");
            return BadRequest($"批量回测失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 获取或创建简单移动平均策略
    /// </summary>
    private async Task<QuantStrategy> GetOrCreateSimpleStrategy()
    {
        // 先查找是否已存在
        var existingStrategy = await _context.QuantStrategies
            .FirstOrDefaultAsync(s => s.Name == "简单移动平均策略");

        if (existingStrategy != null)
            return existingStrategy;

        // 创建新策略
        var strategy = new QuantStrategy
        {
            Name = "简单移动平均策略",
            Description = "适合新手的简单策略：当短期均线上穿长期均线时买入，下穿时卖出",
            Type = StrategyType.TechnicalIndicator,
            Parameters = """
            {
                "shortPeriod": 5,
                "longPeriod": 20,
                "fastPeriod": 12,
                "slowPeriod": 26,
                "signalPeriod": 9,
                "rsiPeriod": 14,
                "overboughtThreshold": 70,
                "oversoldThreshold": 30,
                "bollingerPeriod": 20,
                "standardDeviation": 2
            }
            """,
            InitialCapital = 100000,
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        _context.QuantStrategies.Add(strategy);
        await _context.SaveChangesAsync();

        _logger.LogInformation("创建了新的简单移动平均策略，ID: {StrategyId}", strategy.Id);
        return strategy;
    }
}

public class QuickBacktestRequest
{
    public string StockCode { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.Now.AddMonths(-6);
    public DateTime EndDate { get; set; } = DateTime.Now;
    public decimal InitialCapital { get; set; } = 100000;
}

public class QuickBatchBacktestRequest
{
    public List<string> StockCodes { get; set; } = new();
    public DateTime StartDate { get; set; } = DateTime.Now.AddMonths(-6);
    public DateTime EndDate { get; set; } = DateTime.Now;
    public decimal InitialCapital { get; set; } = 100000;
}