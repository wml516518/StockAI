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
    public async Task<ActionResult<PagedResult<Stock>>> Search([FromBody] ScreenCriteria criteria)
    {
        try
        {
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
}


