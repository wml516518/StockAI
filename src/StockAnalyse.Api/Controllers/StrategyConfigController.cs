using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

/// <summary>
/// 策略配置管理控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StrategyConfigController : ControllerBase
{
    private readonly IStrategyConfigService _strategyConfigService;
    private readonly IQuantTradingService _quantTradingService;
    private readonly ILogger<StrategyConfigController> _logger;

    public StrategyConfigController(
        IStrategyConfigService strategyConfigService,
        IQuantTradingService quantTradingService,
        ILogger<StrategyConfigController> logger)
    {
        _strategyConfigService = strategyConfigService;
        _quantTradingService = quantTradingService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有策略配置文件
    /// </summary>
    [HttpGet("configs")]
    public async Task<ActionResult<List<StrategyConfig>>> GetAllConfigs()
    {
        try
        {
            var configs = await _strategyConfigService.LoadDefaultStrategiesAsync();
            return Ok(configs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取策略配置失败");
            return StatusCode(500, "获取策略配置失败");
        }
    }

    /// <summary>
    /// 根据名称获取策略配置
    /// </summary>
    [HttpGet("configs/{strategyName}")]
    public async Task<ActionResult<StrategyConfig>> GetConfigByName(string strategyName)
    {
        try
        {
            var config = await _strategyConfigService.LoadStrategyByNameAsync(strategyName);
            if (config == null)
            {
                return NotFound($"策略配置不存在: {strategyName}");
            }
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取策略配置失败: {Name}", strategyName);
            return StatusCode(500, "获取策略配置失败");
        }
    }

    /// <summary>
    /// 保存策略配置
    /// </summary>
    [HttpPost("configs")]
    public async Task<ActionResult> SaveConfig([FromBody] Models.StrategyConfig config)
    {
        try
        {
            var success = await _strategyConfigService.SaveStrategyConfigAsync(config);
            if (success)
            {
                return Ok("策略配置保存成功");
            }
            return StatusCode(500, "策略配置保存失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存策略配置失败: {Name}", config.Name);
            return StatusCode(500, "保存策略配置失败");
        }
    }

    /// <summary>
    /// 删除策略配置文件
    /// </summary>
    [HttpDelete("configs/{fileName}")]
    public async Task<ActionResult> DeleteConfig(string fileName)
    {
        try
        {
            var success = await _strategyConfigService.DeleteStrategyConfigAsync(fileName);
            if (success)
            {
                return Ok("策略配置删除成功");
            }
            return NotFound("策略配置文件不存在");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除策略配置失败: {File}", fileName);
            return StatusCode(500, "删除策略配置失败");
        }
    }

    /// <summary>
    /// 获取可用的配置文件列表
    /// </summary>
    [HttpGet("files")]
    public async Task<ActionResult<List<string>>> GetAvailableFiles()
    {
        try
        {
            var files = await _strategyConfigService.GetAvailableConfigFilesAsync();
            return Ok(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取配置文件列表失败");
            return StatusCode(500, "获取配置文件列表失败");
        }
    }

    /// <summary>
    /// 从配置文件导入策略到数据库
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult> ImportStrategies()
    {
        try
        {
            var importedCount = await _strategyConfigService.ImportStrategiesToDatabaseAsync();
            return Ok(new { message = $"成功导入 {importedCount} 个策略", count = importedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入策略失败");
            return StatusCode(500, "导入策略失败");
        }
    }

    /// <summary>
    /// 从配置文件创建策略实例
    /// </summary>
    [HttpPost("create-from-config/{configName}")]
    public async Task<ActionResult> CreateStrategyFromConfig(string configName)
    {
        try
        {
            var config = await _strategyConfigService.LoadStrategyByNameAsync(configName);
            if (config == null)
            {
                return NotFound($"策略配置不存在: {configName}");
            }

            var strategy = _strategyConfigService.ConvertToQuantStrategy(config);
            var createdStrategy = await _quantTradingService.CreateStrategyAsync(strategy);
            
            return Ok(new { message = "策略创建成功", strategy = createdStrategy });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从配置创建策略失败: {Name}", configName);
            return StatusCode(500, "从配置创建策略失败");
        }
    }
}