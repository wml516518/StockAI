using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BacktestController : ControllerBase
{
    private readonly StockDbContext _context;
    private readonly IBacktestService _backtestService;
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<BacktestController> _logger;

    public BacktestController(
        StockDbContext context,
        IBacktestService backtestService,
        IStockDataService stockDataService,
        ILogger<BacktestController> logger)
    {
        _context = context;
        _backtestService = backtestService;
        _stockDataService = stockDataService;
        _logger = logger;
    }

    public class RunRequest
    {
        public string StockCode { get; set; } = string.Empty;
        public string StrategyName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal InitialCapital { get; set; } = 100000;
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StrategyName))
            return BadRequest("策略名称不能为空");
        if (request.StartDate > request.EndDate)
            return BadRequest("开始日期不能晚于结束日期");

        var strategy = await _context.QuantStrategies.FirstOrDefaultAsync(s => s.Name == request.StrategyName);
        if (strategy == null)
            return NotFound($"未找到策略：{request.StrategyName}");

        // 确保回测期间自选股的历史数据充足
        var watchlistCodes = await _context.WatchlistStocks
            .Select(w => w.StockCode)
            .Distinct()
            .ToListAsync();

        if (!watchlistCodes.Any())
        {
            _logger.LogWarning("自选股为空，回测无法进行。请先添加股票到自选股。");
            return BadRequest("没有自选股可供回测，请先在“自选股”页面添加股票。");
        }

        foreach (var code in watchlistCodes)
        {
            try
            {
                await _stockDataService.FetchAndStoreDailyHistoryAsync(code, request.StartDate.AddDays(-60), request.EndDate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "拉取 {Code} 历史数据失败，回测可能数据不足", code);
            }
        }

        var result = await _backtestService.RunBacktestAsync(strategy.Id, request.StartDate, request.EndDate, request.InitialCapital);

        // 解析详细结果中的交易列表，返回前端期望结构
        var trades = new List<object>();
        try
        {
            if (!string.IsNullOrEmpty(result.DetailedResults))
            {
                using var doc = JsonDocument.Parse(result.DetailedResults);
                if (doc.RootElement.TryGetProperty("Trades", out var t))
                {
                    foreach (var item in t.EnumerateArray())
                    {
                        trades.Add(new
                        {
                            StockCode = item.GetProperty("StockCode").GetString(),
                            Type = item.GetProperty("Type").GetString(),
                            Quantity = item.GetProperty("Quantity").GetDecimal(),
                            Price = item.GetProperty("Price").GetDecimal(),
                            Amount = item.GetProperty("Amount").GetDecimal(),
                            ExecutedAt = item.GetProperty("ExecutedAt").GetDateTime()
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析回测详情失败");
        }

        return Ok(new
        {
            totalReturn = result.TotalReturn,
            annualizedReturn = result.AnnualizedReturn,
            maxDrawdown = result.MaxDrawdown,
            sharpeRatio = result.SharpeRatio,
            totalTrades = result.TotalTrades,
            winRate = result.WinRate,
            trades = trades
        });
    }
}