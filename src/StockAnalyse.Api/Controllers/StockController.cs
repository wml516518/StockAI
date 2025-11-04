using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StockController : ControllerBase
{
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<StockController> _logger;

    public StockController(IStockDataService stockDataService, ILogger<StockController> logger)
    {
        _stockDataService = stockDataService;
        _logger = logger;
    }

    /// <summary>
    /// 获取股票实时行情
    /// </summary>
    [HttpGet("{code}")]
    public async Task<ActionResult<Stock>> GetStock(string code)
    {
        var stock = await _stockDataService.GetRealTimeQuoteAsync(code);
        if (stock == null)
        {
            return NotFound();
        }
        return Ok(stock);
    }

    /// <summary>
    /// 批量获取股票行情
    /// </summary>
    [HttpPost("batch")]
    public async Task<ActionResult<List<Stock>>> GetBatchStocks([FromBody] List<string> codes)
    {
        var stocks = await _stockDataService.GetBatchQuotesAsync(codes);
        return Ok(stocks);
    }

    /// <summary>
    /// 获取日线数据
    /// </summary>
    [HttpGet("{code}/history")]
    public async Task<ActionResult<List<StockHistory>>> GetHistory(string code, DateTime? startDate, DateTime? endDate)
    {
        var start = startDate ?? DateTime.Now.AddDays(-30);
        var end = endDate ?? DateTime.Now;
        
        var histories = await _stockDataService.GetDailyDataAsync(code, start, end);
        return Ok(histories);
    }

    /// <summary>
    /// 获取排名列表
    /// </summary>
    [HttpGet("ranking/{market}")]
    public async Task<ActionResult<List<Stock>>> GetRanking(string market, int top = 100)
    {
        var stocks = await _stockDataService.GetRankingListAsync(market, top);
        return Ok(stocks);
    }

    /// <summary>
    /// 计算MACD指标（注意：不再更新数据库中的MACD字段）
    /// </summary>
    [HttpPost("{code}/macd")]
    public async Task<ActionResult<object>> CalculateMACD(string code)
    {
        var result = await _stockDataService.CalculateMACDAsync(code);
        return Ok(new { Macd = result.macd, Signal = result.signal, Histogram = result.histogram });
    }

    /// <summary>
    /// 获取股票基本面信息
    /// </summary>
    [HttpGet("{code}/fundamental")]
    public async Task<ActionResult<StockFundamentalInfo>> GetFundamental(string code)
    {
        var info = await _stockDataService.GetFundamentalInfoAsync(code);
        if (info == null)
        {
            return NotFound();
        }
        return Ok(info);
    }
}

