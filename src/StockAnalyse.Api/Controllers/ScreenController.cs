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
    /// 条件选股
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<List<Stock>>> Search([FromBody] ScreenCriteria criteria)
    {
        try
        {
            _logger.LogInformation("收到选股请求，条件: {Criteria}", 
                System.Text.Json.JsonSerializer.Serialize(criteria));
            
            var stocks = await _screenService.ScreenStocksAsync(criteria);
            
            _logger.LogInformation("选股查询完成，返回 {Count} 条结果", stocks.Count);
            
            return Ok(stocks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "选股查询时发生错误");
            return StatusCode(500, new { error = "选股查询失败", message = ex.Message });
        }
    }
}


