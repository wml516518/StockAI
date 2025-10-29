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
        var stocks = await _screenService.ScreenStocksAsync(criteria);
        return Ok(stocks);
    }
}


