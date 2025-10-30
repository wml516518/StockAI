using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIPromptController : ControllerBase
{
    private readonly AIPromptConfigService _configService;
    private readonly ILogger<AIPromptController> _logger;

    public AIPromptController(AIPromptConfigService configService, ILogger<AIPromptController> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AIPromptSettings>> Get()
    {
        var settings = await _configService.GetSettingsAsync();
        return Ok(settings);
    }

    [HttpPut]
    public async Task<ActionResult> Update([FromBody] AIPromptSettings settings)
    {
        await _configService.SaveSettingsAsync(settings);
        return NoContent();
    }
}