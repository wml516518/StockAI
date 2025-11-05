using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var alert = await _alertService.CreateAlertAsync(request.StockCode, request.TargetPrice, request.Type);
            return Ok(alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建价格提醒失败");
            return StatusCode(500, new { error = "创建价格提醒失败", message = ex.Message });
        }
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
        await _alertService.CheckSuggestedPriceAlertsAsync();
        return Ok(new { message = "检查完成" });
    }
}

public class CreateAlertRequest
{
    [Required(ErrorMessage = "股票代码不能为空")]
    public string StockCode { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "目标价格不能为空")]
    [Range(0.01, double.MaxValue, ErrorMessage = "目标价格必须大于0")]
    public decimal TargetPrice { get; set; }
    
    [Required(ErrorMessage = "提醒类型不能为空")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AlertType Type { get; set; }
}

