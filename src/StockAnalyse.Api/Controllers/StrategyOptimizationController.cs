using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StrategyOptimizationController : ControllerBase
{
    private readonly IStrategyOptimizationService _optimizationService;
    private readonly ILogger<StrategyOptimizationController> _logger;

    public StrategyOptimizationController(
        IStrategyOptimizationService optimizationService,
        ILogger<StrategyOptimizationController> logger)
    {
        _optimizationService = optimizationService;
        _logger = logger;
    }

    /// <summary>
    /// 优化策略参数
    /// </summary>
    [HttpPost("optimize")]
    public async Task<ActionResult<StrategyOptimizationResult>> OptimizeStrategy([FromBody] OptimizeStrategyRequest request)
    {
        try
        {
            if (request.StartDate >= request.EndDate)
                return BadRequest("开始日期必须早于结束日期");

            if (request.StockCodes == null || request.StockCodes.Count == 0)
                return BadRequest("必须指定至少一个股票代码");

            var result = await _optimizationService.OptimizeStrategyAsync(
                request.StrategyId,
                request.StockCodes,
                request.StartDate,
                request.EndDate,
                request.OptimizationConfig ?? new OptimizationConfig());

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "优化策略时发生错误");
            return StatusCode(500, "优化策略时发生错误：" + ex.Message);
        }
    }

    /// <summary>
    /// 批量优化策略
    /// </summary>
    [HttpPost("batch-optimize")]
    public async Task<ActionResult<List<StrategyOptimizationResult>>> BatchOptimizeStrategies([FromBody] BatchOptimizeRequest request)
    {
        try
        {
            if (request.StartDate >= request.EndDate)
                return BadRequest("开始日期必须早于结束日期");

            if (request.StockCodes == null || request.StockCodes.Count == 0)
                return BadRequest("必须指定至少一个股票代码");

            if (request.StrategyIds == null || request.StrategyIds.Count == 0)
                return BadRequest("必须指定至少一个策略ID");

            var results = await _optimizationService.BatchOptimizeStrategiesAsync(
                request.StrategyIds,
                request.StockCodes,
                request.StartDate,
                request.EndDate,
                request.OptimizationConfig ?? new OptimizationConfig());

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量优化策略时发生错误");
            return StatusCode(500, "批量优化策略时发生错误：" + ex.Message);
        }
    }

    /// <summary>
    /// 获取策略优化历史
    /// </summary>
    [HttpGet("{strategyId}/history")]
    public async Task<ActionResult<List<StrategyOptimizationResult>>> GetOptimizationHistory(int strategyId)
    {
        try
        {
            var history = await _optimizationService.GetOptimizationHistoryAsync(strategyId);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取优化历史时发生错误");
            return StatusCode(500, "获取优化历史时发生错误：" + ex.Message);
        }
    }

    /// <summary>
    /// 应用最优参数到策略
    /// </summary>
    [HttpPost("{strategyId}/apply/{optimizationResultId}")]
    public async Task<ActionResult> ApplyOptimalParameters(int strategyId, int optimizationResultId)
    {
        try
        {
            var success = await _optimizationService.ApplyOptimalParametersAsync(strategyId, optimizationResultId);
            
            if (!success)
                return BadRequest("应用参数失败，请检查策略ID和优化结果ID是否正确");

            return Ok(new { message = "参数应用成功" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用最优参数时发生错误");
            return StatusCode(500, "应用最优参数时发生错误：" + ex.Message);
        }
    }

    /// <summary>
    /// 获取默认优化配置
    /// </summary>
    [HttpGet("default-config")]
    public ActionResult<OptimizationConfig> GetDefaultConfig()
    {
        return Ok(new OptimizationConfig());
    }
}

/// <summary>
/// 优化策略请求模型
/// </summary>
public class OptimizeStrategyRequest
{
    public int StrategyId { get; set; }
    public List<string> StockCodes { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public OptimizationConfig? OptimizationConfig { get; set; }
}

/// <summary>
/// 批量优化请求模型
/// </summary>
public class BatchOptimizeRequest
{
    public List<int> StrategyIds { get; set; } = new();
    public List<string> StockCodes { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public OptimizationConfig? OptimizationConfig { get; set; }
}