using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertController : ControllerBase
{
    private readonly IPriceAlertService _alertService;
    private readonly ILogger<AlertController> _logger;

    public AlertController(IPriceAlertService alertService, ILogger<AlertController> logger)
    {
        _alertService = alertService;
        _logger = logger;
    }

    /// <summary>
    /// 创建价格提醒
    /// </summary>
    [HttpPost("create")]
    public async Task<ActionResult<PriceAlert>> CreateAlert([FromBody] CreateAlertRequest request)
    {
        var alert = await _alertService.CreateAlertAsync(request.StockCode, request.TargetPrice, request.Type);
        return Ok(alert);
    }

    /// <summary>
    /// 获取所有活跃提醒
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<List<PriceAlert>>> GetActiveAlerts()
    {
        var alerts = await _alertService.GetActiveAlertsAsync();
        return Ok(alerts);
    }

    /// <summary>
    /// 删除提醒
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteAlert(int id)
    {
        var result = await _alertService.DeleteAlertAsync(id);
        if (!result)
        {
            return NotFound();
        }
        return NoContent();
    }

    /// <summary>
    /// 触发价格提醒检查
    /// </summary>
    [HttpPost("check")]
    public async Task<ActionResult> CheckAlerts()
    {
        await _alertService.CheckAndTriggerAlertsAsync();
        return Ok(new { message = "检查完成" });
    }
}

public class CreateAlertRequest
{
    public string StockCode { get; set; } = string.Empty;
    public decimal TargetPrice { get; set; }
    public AlertType Type { get; set; }
}

