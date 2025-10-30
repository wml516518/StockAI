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

    public class RunBatchRequest
    {
        public List<string>? StockCodes { get; set; } // 多选或多码输入
        public string StrategyName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal InitialCapital { get; set; } = 100000;
    }

    [HttpPost("run-batch")]
    public async Task<IActionResult> RunBatch([FromBody] RunBatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StrategyName))
            return BadRequest("策略名称不能为空");
        if (request.StartDate > request.EndDate)
            return BadRequest("开始日期不能晚于结束日期");

        var strategy = await _context.QuantStrategies.FirstOrDefaultAsync(s => s.Name == request.StrategyName);
        if (strategy == null)
            return NotFound($"未找到策略：{request.StrategyName}");

        // 目标股票列表：优先使用请求，否则回退自选股
        var targetCodes = (request.StockCodes != null && request.StockCodes.Any())
            ? request.StockCodes.Distinct().ToList()
            : await _context.WatchlistStocks
                .Select(w => w.StockCode)
                .Distinct()
                .ToListAsync();

        if (!targetCodes.Any())
            return BadRequest("没有可供回测的股票，请传入股票列表或在“自选股”添加股票。");

        // 调用批量回测
        var summaries = await _backtestService.RunBatchBacktestAsync(
            strategy.Id, request.StartDate, request.EndDate, request.InitialCapital, targetCodes);

        return Ok(summaries);
    }
}