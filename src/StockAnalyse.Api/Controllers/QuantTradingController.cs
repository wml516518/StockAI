using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuantTradingController : ControllerBase
{
    private readonly IQuantTradingService _quantTradingService;
    private readonly ITechnicalIndicatorService _technicalIndicatorService;
    private readonly ILogger<QuantTradingController> _logger;

    public QuantTradingController(
        IQuantTradingService quantTradingService,
        ITechnicalIndicatorService technicalIndicatorService,
        ILogger<QuantTradingController> logger)
    {
        _quantTradingService = quantTradingService;
        _technicalIndicatorService = technicalIndicatorService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有策略
    /// </summary>
    [HttpGet("strategies")]
    public async Task<IActionResult> GetAllStrategies()
    {
        try
        {
            var strategies = await _quantTradingService.GetAllStrategiesAsync();
            return Ok(strategies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取策略列表失败");
            return StatusCode(500, "获取策略列表失败");
        }
    }

    /// <summary>
    /// 根据ID获取策略
    /// </summary>
    [HttpGet("strategies/{id}")]
    public async Task<IActionResult> GetStrategy(int id)
    {
        try
        {
            var strategy = await _quantTradingService.GetStrategyByIdAsync(id);
            if (strategy == null)
                return NotFound("策略不存在");

            return Ok(strategy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取策略失败: {Id}", id);
            return StatusCode(500, "获取策略失败");
        }
    }

    /// <summary>
    /// 创建策略
    /// </summary>
    [HttpPost("strategies")]
    public async Task<IActionResult> CreateStrategy([FromBody] CreateStrategyRequest request)
    {
        try
        {
            var strategy = new QuantStrategy
            {
                Name = request.Name,
                Description = request.Description,
                Type = request.Type,
                Parameters = JsonSerializer.Serialize(request.Parameters),
                InitialCapital = request.InitialCapital,
                CurrentCapital = request.InitialCapital,
                IsActive = request.IsActive
            };

            var createdStrategy = await _quantTradingService.CreateStrategyAsync(strategy);
            return CreatedAtAction(nameof(GetStrategy), new { id = createdStrategy.Id }, createdStrategy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建策略失败");
            return StatusCode(500, "创建策略失败");
        }
    }

    /// <summary>
    /// 更新策略
    /// </summary>
    [HttpPut("strategies/{id}")]
    public async Task<IActionResult> UpdateStrategy(int id, [FromBody] UpdateStrategyRequest request)
    {
        try
        {
            var strategy = await _quantTradingService.GetStrategyByIdAsync(id);
            if (strategy == null)
                return NotFound("策略不存在");

            strategy.Name = request.Name;
            strategy.Description = request.Description;
            strategy.Parameters = JsonSerializer.Serialize(request.Parameters);
            strategy.IsActive = request.IsActive;

            var updatedStrategy = await _quantTradingService.UpdateStrategyAsync(strategy);
            return Ok(updatedStrategy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新策略失败: {Id}", id);
            return StatusCode(500, "更新策略失败");
        }
    }

    /// <summary>
    /// 删除策略
    /// </summary>
    [HttpDelete("strategies/{id}")]
    public async Task<IActionResult> DeleteStrategy(int id)
    {
        try
        {
            var result = await _quantTradingService.DeleteStrategyAsync(id);
            if (!result)
                return NotFound("策略不存在");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除策略失败: {Id}", id);
            return StatusCode(500, "删除策略失败");
        }
    }

    /// <summary>
    /// 运行策略
    /// </summary>
    [HttpPost("strategies/{id}/run")]
    public async Task<IActionResult> RunStrategy(int id, [FromBody] RunStrategyRequest? request = null)
    {
        try
        {
            var signals = await _quantTradingService.RunStrategyAsync(id, request?.StockCodes);
            return Ok(new { SignalCount = signals.Count, Signals = signals });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "运行策略失败: {Id}", id);
            return StatusCode(500, "运行策略失败");
        }
    }

    /// <summary>
    /// 执行交易信号
    /// </summary>
    [HttpPost("signals/{signalId}/execute")]
    public async Task<IActionResult> ExecuteSignal(int signalId)
    {
        try
        {
            var trade = await _quantTradingService.ExecuteSignalAsync(signalId);
            if (trade == null)
                return BadRequest("信号执行失败或已执行");

            return Ok(trade);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行信号失败: {SignalId}", signalId);
            return StatusCode(500, "执行信号失败");
        }
    }

    /// <summary>
    /// 获取策略的交易信号
    /// </summary>
    [HttpGet("strategies/{id}/signals")]
    public async Task<IActionResult> GetStrategySignals(int id, [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var signals = await _quantTradingService.GetStrategySignalsAsync(id, startDate, endDate);
            return Ok(signals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取策略信号失败: {Id}", id);
            return StatusCode(500, "获取策略信号失败");
        }
    }

    /// <summary>
    /// 获取策略的交易记录
    /// </summary>
    [HttpGet("strategies/{id}/trades")]
    public async Task<IActionResult> GetStrategyTrades(int id, [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var trades = await _quantTradingService.GetStrategyTradesAsync(id, startDate, endDate);
            return Ok(trades);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取策略交易记录失败: {Id}", id);
            return StatusCode(500, "获取策略交易记录失败");
        }
    }

    /// <summary>
    /// 获取技术指标数据
    /// </summary>
    [HttpGet("indicators/{stockCode}/sma")]
    public async Task<IActionResult> GetSMA(string stockCode, [FromQuery] int period = 20, [FromQuery] int count = 100)
    {
        try
        {
            var sma = await _technicalIndicatorService.CalculateSMAAsync(stockCode, period, count);
            return Ok(sma);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取SMA数据失败: {StockCode}", stockCode);
            return StatusCode(500, "获取SMA数据失败");
        }
    }

    /// <summary>
    /// 获取MACD数据
    /// </summary>
    [HttpGet("indicators/{stockCode}/macd")]
    public async Task<IActionResult> GetMACD(string stockCode, [FromQuery] int fastPeriod = 12, [FromQuery] int slowPeriod = 26, [FromQuery] int signalPeriod = 9, [FromQuery] int count = 100)
    {
        try
        {
            var macd = await _technicalIndicatorService.CalculateMACDAsync(stockCode, fastPeriod, slowPeriod, signalPeriod, count);
            return Ok(macd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取MACD数据失败: {StockCode}", stockCode);
            return StatusCode(500, "获取MACD数据失败");
        }
    }

    /// <summary>
    /// 获取RSI数据
    /// </summary>
    [HttpGet("indicators/{stockCode}/rsi")]
    public async Task<IActionResult> GetRSI(string stockCode, [FromQuery] int period = 14, [FromQuery] int count = 100)
    {
        try
        {
            var rsi = await _technicalIndicatorService.CalculateRSIAsync(stockCode, period, count);
            return Ok(rsi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取RSI数据失败: {StockCode}", stockCode);
            return StatusCode(500, "获取RSI数据失败");
        }
    }
}

/// <summary>
/// 创建策略请求模型
/// </summary>
public class CreateStrategyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public StrategyType Type { get; set; }
    public TechnicalIndicatorParameters Parameters { get; set; } = new();
    public decimal InitialCapital { get; set; } = 100000;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 更新策略请求模型
/// </summary>
public class UpdateStrategyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TechnicalIndicatorParameters Parameters { get; set; } = new();
    public bool IsActive { get; set; }
}

/// <summary>
/// 运行策略请求模型
/// </summary>
public class RunStrategyRequest
{
    public List<string>? StockCodes { get; set; }
}