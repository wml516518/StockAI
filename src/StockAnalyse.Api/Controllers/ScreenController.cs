using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScreenController : ControllerBase
{
    private readonly IScreenService _screenService;
    private readonly ILogger<ScreenController> _logger;

    public ScreenController(IScreenService screenService, ILogger<ScreenController> logger)
    {
        _screenService = screenService;
        _logger = logger;
    }

    /// <summary>
    /// 条件选股（分页）
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<PagedResult<Stock>>> Search([FromBody] ScreenCriteria? criteria)
    {
        try
        {
            // 如果criteria为null，返回400错误
            if (criteria == null)
            {
                _logger.LogWarning("收到空的选股请求");
                return BadRequest(new { error = "请求体不能为空", message = "请提供选股条件" });
            }
            
            // 确保分页参数有效
            if (criteria.PageIndex < 1)
            {
                criteria.PageIndex = 1;
            }
            if (criteria.PageSize < 1 || criteria.PageSize > 100)
            {
                criteria.PageSize = Math.Clamp(criteria.PageSize, 1, 100);
            }
            
            _logger.LogInformation("收到选股请求，条件: {Criteria}, 页码: {PageIndex}, 每页: {PageSize}", 
                System.Text.Json.JsonSerializer.Serialize(criteria), criteria.PageIndex, criteria.PageSize);
            
            var result = await _screenService.ScreenStocksAsync(criteria);
            
            _logger.LogInformation("选股查询完成，总记录数: {TotalCount}, 当前页: {PageIndex}, 返回 {Count} 条结果", 
                result.TotalCount, result.PageIndex, result.Items.Count);
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "选股查询时发生错误");
            return StatusCode(500, new { error = "选股查询失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 热点题材成交量放大短线策略（依赖Python AKShare服务）
    /// </summary>
    [HttpGet("short-term/hot-volume-breakout")]
    public async Task<IActionResult> GetShortTermHotStrategy([FromQuery] int topHot = 60, [FromQuery] int topThemes = 3, [FromQuery] int themeMembers = 3)
    {
        try
        {
            _logger.LogInformation("请求短线热点策略: topHot={TopHot}, topThemes={TopThemes}, themeMembers={ThemeMembers}", topHot, topThemes, themeMembers);
            var jsonPayload = await _screenService.GetShortTermHotStrategyAsync(topHot, topThemes, themeMembers);
            return Content(jsonPayload, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取短线热点策略失败");
            return StatusCode(500, new { error = "short_term_strategy_failed", message = ex.Message });
        }
    }
}


