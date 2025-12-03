using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScreenController : ControllerBase
{
    private readonly IScreenService _screenService;
    private readonly IAutoSelectionService _autoSelectionService;
    private readonly IAutoFilterService _autoFilterService;
    private readonly ILogger<ScreenController> _logger;

    public ScreenController(
        IScreenService screenService,
        IAutoSelectionService autoSelectionService,
        IAutoFilterService autoFilterService,
        ILogger<ScreenController> logger)
    {
        _screenService = screenService;
        _autoSelectionService = autoSelectionService;
        _autoFilterService = autoFilterService;
        _logger = logger;
    }

    /// <summary>
    /// 测试接口
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { message = "pong", time = DateTime.Now });
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
    
    /// <summary>
    /// 执行自动选股
    /// </summary>
    [HttpPost("auto-selection/execute")]
    public async Task<ActionResult<AutoSelectionResult>> ExecuteAutoSelection()
    {
        try
        {
            _logger.LogInformation("收到手动执行自动选股请求");
            var result = await _autoSelectionService.ExecuteSelectionAsync();
            
            if (!result.Success)
            {
                return BadRequest(new { error = "自动选股失败", message = result.ErrorMessage });
            }
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行自动选股时发生错误");
            return StatusCode(500, new { error = "自动选股执行失败", message = ex.Message });
        }
    }

    /// <summary>
    /// AI选股：使用自然语言描述选股条件，AI解析后执行选股
    /// </summary>
    [HttpPost("ai-search")]
    public async Task<ActionResult<PagedResult<Stock>>> AISearch([FromBody] AISearchRequest? request)
    {
        try
        {
            // 读取原始请求体（用于调试）
            string? rawBody = null;
            try
            {
                if (Request.Body.CanSeek)
                {
                    Request.Body.Position = 0;
                    using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8, leaveOpen: true);
                    rawBody = await reader.ReadToEndAsync();
                    Request.Body.Position = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "无法读取原始请求体");
            }

            // 记录原始请求信息（用于调试）
            _logger.LogInformation("收到AI选股请求，Raw Body: {RawBody}, Parsed Request: {RequestBody}", 
                rawBody ?? "无法读取",
                request != null ? System.Text.Json.JsonSerializer.Serialize(request) : "null");
            
            // 检查ModelState验证错误
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .SelectMany(x => x.Value!.Errors.Select(e => $"{x.Key}: {e.ErrorMessage}"))
                    .ToList();
                
                _logger.LogWarning("AI选股请求ModelState验证失败: {Errors}, Raw Body: {RawBody}", 
                    string.Join(", ", errors), rawBody ?? "无法读取");
                return BadRequest(new { error = "请求格式错误", message = "请求参数验证失败", details = errors, rawBody });
            }

            if (request == null)
            {
                _logger.LogWarning("收到空的AI选股请求（request为null），Raw Body: {RawBody}", rawBody ?? "无法读取");
                return BadRequest(new { error = "请求参数不能为空", message = "请求体不能为空，请提供自然语言选股条件", rawBody });
            }

            if (string.IsNullOrWhiteSpace(request.NaturalLanguage))
            {
                _logger.LogWarning("收到空的AI选股请求（NaturalLanguage为空），Request: {Request}", 
                    System.Text.Json.JsonSerializer.Serialize(request));
                return BadRequest(new { error = "请求参数不能为空", message = "请提供自然语言选股条件" });
            }

            _logger.LogInformation("收到AI选股请求: NaturalLanguage={NaturalLanguage}, PageIndex={PageIndex}, PageSize={PageSize}, ModelId={ModelId}", 
                request.NaturalLanguage, request.PageIndex, request.PageSize, request.ModelId);

            // 使用AI解析自然语言为选股条件
            ScreenCriteria criteria;
            try
            {
                criteria = await _screenService.ParseNaturalLanguageToCriteriaAsync(
                    request.NaturalLanguage, 
                    request.ModelId);
            }
            catch (InvalidOperationException ex)
            {
                // AI解析失败，返回400错误
                _logger.LogWarning("AI解析自然语言失败: {Message}", ex.Message);
                return BadRequest(new { error = "AI解析失败", message = ex.Message });
            }

            // 应用分页参数（如果请求中指定了）
            if (request.PageIndex.HasValue && request.PageIndex.Value > 0)
            {
                criteria.PageIndex = request.PageIndex.Value;
            }
            if (request.PageSize.HasValue && request.PageSize.Value > 0)
            {
                criteria.PageSize = Math.Min(request.PageSize.Value, 100);
            }

            // 执行选股
            var result = await _screenService.ScreenStocksAsync(criteria);

            _logger.LogInformation("AI选股查询完成，总记录数: {TotalCount}, 当前页: {PageIndex}, 返回 {Count} 条结果",
                result.TotalCount, result.PageIndex, result.Items.Count);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI选股查询时发生错误: {Message}", ex.Message);
            
            // 如果是InvalidOperationException（AI解析失败），返回400
            if (ex is InvalidOperationException)
            {
                return BadRequest(new { error = "AI解析失败", message = ex.Message });
            }
            
            // 其他错误返回500
            return StatusCode(500, new { error = "AI选股查询失败", message = ex.Message });
        }
    }
}

/// <summary>
/// 自动筛选请求模型
/// </summary>
public class AutoFilterRequest
{
    /// <summary>
    /// 待筛选的股票代码列表（如果为空，则从全市场筛选）
    /// </summary>
    public List<string>? StockCodes { get; set; }

    /// <summary>
    /// 是否启用社交舆情过滤（可选）
    /// </summary>
    public bool EnableSentimentFilter { get; set; } = false;
}

/// <summary>
/// 股票筛选检查结果
/// </summary>
public class StockFilterCheckResult
{
    public string StockCode { get; set; } = string.Empty;
    public FundamentalFilterResult Fundamental { get; set; } = new();
    public TechnicalFilterResult Technical { get; set; } = new();
    public NewsFilterResult News { get; set; } = new();
    public bool Passed { get; set; }
}

/// <summary>
/// AI选股请求模型
/// </summary>
public class AISearchRequest
{
    /// <summary>
    /// 自然语言描述的选股条件
    /// </summary>
    public string NaturalLanguage { get; set; } = string.Empty;

    /// <summary>
    /// 可选：指定使用的AI模型ID
    /// </summary>
    public int? ModelId { get; set; }

    /// <summary>
    /// 可选：页码（默认1）
    /// </summary>
    public int? PageIndex { get; set; }

    /// <summary>
    /// 可选：每页数量（默认10）
    /// </summary>
    public int? PageSize { get; set; }
}


