using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WatchlistController : ControllerBase
{
    private readonly IWatchlistService _watchlistService;
    private readonly ITradingPlanService _tradingPlanService;
    private readonly StockDbContext _context;
    private readonly ILogger<WatchlistController> _logger;
    private readonly TradingPlanEventService _eventService;

    public WatchlistController(
        IWatchlistService watchlistService, 
        ITradingPlanService tradingPlanService,
        StockDbContext context,
        ILogger<WatchlistController> logger,
        TradingPlanEventService eventService)
    {
        _watchlistService = watchlistService;
        _tradingPlanService = tradingPlanService;
        _context = context;
        _logger = logger;
        _eventService = eventService;
    }

    /// <summary>
    /// 添加自选股
    /// </summary>
    [HttpPost("add")]
    public async Task<ActionResult<WatchlistStock>> AddStock([FromBody] AddWatchlistRequest request)
    {
        try
        {
            var watchlistStock = await _watchlistService.AddToWatchlistAsync(
                request.StockCode, 
                request.CategoryId, 
                request.CostPrice, 
                request.Quantity);
            return Ok(watchlistStock);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// 移除自选股
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> RemoveStock(int id)
    {
        var result = await _watchlistService.RemoveFromWatchlistAsync(id);
        if (!result)
        {
            return NotFound();
        }
        return NoContent();
    }

    /// <summary>
    /// 获取所有自选股
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<WatchlistStock>>> GetWatchlist()
    {
        try
        {
            var grouped = await _watchlistService.GetWatchlistGroupedByCategoryAsync();
            // 将所有分类的自选股合并成一个列表
            var allStocks = grouped.Values.SelectMany(stocks => stocks).ToList();
            _logger.LogInformation("获取自选股列表，共 {Count} 条", allStocks.Count);
            return Ok(allStocks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取自选股列表失败");
            return StatusCode(500, new { error = "获取自选股列表失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取所有自选股（按分类）
    /// </summary>
    [HttpGet("grouped")]
    public async Task<ActionResult<Dictionary<string, List<WatchlistStock>>>> GetGroupedWatchlist()
    {
        var result = await _watchlistService.GetWatchlistGroupedByCategoryAsync();
        return Ok(result);
    }

    /// <summary>
    /// 获取分类的自选股
    /// </summary>
    [HttpGet("category/{categoryId}")]
    public async Task<ActionResult<List<WatchlistStock>>> GetByCategory(int categoryId)
    {
        var stocks = await _watchlistService.GetWatchlistByCategoryAsync(categoryId);
        return Ok(stocks);
    }

    /// <summary>
    /// 更新成本信息
    /// </summary>
    [HttpPut("{id}/cost")]
    public async Task<ActionResult<WatchlistStock>> UpdateCost(int id, [FromBody] UpdateCostRequest request)
    {
        try
        {
            var stock = await _watchlistService.UpdateCostInfoAsync(id, request.CostPrice, request.Quantity);
            return Ok(stock);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 更新价格提醒
    /// </summary>
    [HttpPut("{id}/alert")]
    public async Task<ActionResult<WatchlistStock>> UpdateAlert(int id, [FromBody] UpdateAlertRequest request)
    {
        try
        {
            var stock = await _watchlistService.UpdatePriceAlertAsync(id, request.HighAlert, request.LowAlert);
            return Ok(stock);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 重新计算盈亏
    /// </summary>
    [HttpPost("{id}/recalculate")]
    public async Task<ActionResult<WatchlistStock>> Recalculate(int id)
    {
        try
        {
            var stock = await _watchlistService.CalculateProfitLossAsync(id);
            return Ok(stock);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 获取所有分类
    /// </summary>
    [HttpGet("categories")]
    public async Task<ActionResult<List<WatchlistCategory>>> GetCategories()
    {
        var categories = await _watchlistService.GetCategoriesAsync();
        return Ok(categories);
    }

    /// <summary>
    /// 创建分类
    /// </summary>
    [HttpPost("categories")]
    public async Task<ActionResult<WatchlistCategory>> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var category = await _watchlistService.CreateCategoryAsync(request.Name, request.Description, request.Color);
        return Ok(category);
    }

    /// <summary>
    /// 删除分类
    /// </summary>
    [HttpDelete("categories/{id}")]
    public async Task<ActionResult> DeleteCategory(int id)
    {
        try
        {
            var result = await _watchlistService.DeleteCategoryAsync(id);
            if (!result)
            {
                return NotFound();
            }
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// 更新自选股分类
    /// </summary>
    [HttpPut("{id}/category")]
    public async Task<ActionResult<WatchlistStock>> UpdateCategory(int id, [FromBody] UpdateCategoryRequest request)
    {
        try
        {
            var stock = await _watchlistService.UpdateCategoryAsync(id, request.CategoryId);
            return Ok(stock);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// 更新自选股建议价格
    /// </summary>
    [HttpPut("{id}/suggested-price")]
    public async Task<ActionResult<WatchlistStock>> UpdateSuggestedPrice(int id, [FromBody] UpdateSuggestedPriceRequest request)
    {
        try
        {
            var stock = await _watchlistService.UpdateSuggestedPriceAsync(id, request.SuggestedBuyPrice, request.SuggestedSellPrice);
            return Ok(stock);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 重置自选股提醒标志
    /// </summary>
    [HttpPost("{id}/reset-alerts")]
    public async Task<ActionResult<WatchlistStock>> ResetAlertFlags(int id, [FromBody] ResetAlertFlagsRequest request)
    {
        try
        {
            var stock = await _watchlistService.ResetAlertFlagsAsync(id, request.CurrentPrice);
            return Ok(stock);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 开启/关闭一键做T
    /// </summary>
    [HttpPut("{id}/auto-trading")]
    public async Task<ActionResult<WatchlistStock>> ToggleAutoTrading(int id, [FromBody] ToggleAutoTradingRequest request)
    {
        try
        {
            var stock = await _context.WatchlistStocks.FindAsync(id);
            if (stock == null)
            {
                return NotFound("自选股不存在");
            }

            stock.AutoTradingEnabled = request.Enabled;
            stock.AutoTradingIntervalMinutes = request.IntervalMinutes ?? 30;
            
            // 如果开启做T，立即生成一次方案
            if (request.Enabled)
            {
                var plan = await _tradingPlanService.GenerateTradingPlanAsync(stock.StockCode);
                if (plan.Success)
                {
                    stock.TradingPlan = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        buyPriceRange = plan.BuyPriceRange,
                        sellPriceRange = plan.SellPriceRange,
                        suggestion = plan.Suggestion,
                        currentPrice = plan.CurrentPrice,
                        updateTime = plan.UpdateTime
                    });
                    stock.TradingPlanUpdateTime = plan.UpdateTime;
                }
            }
            else
            {
                // 关闭时清空方案
                stock.TradingPlan = null;
                stock.TradingPlanUpdateTime = null;
            }

            stock.LastUpdate = DateTime.Now;
            await _context.SaveChangesAsync();

            return Ok(stock);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换做T状态失败: {Id}", id);
            return StatusCode(500, new { message = $"切换做T状态失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 手动更新做T方案
    /// </summary>
    [HttpPost("{id}/trading-plan/refresh")]
    public async Task<ActionResult<WatchlistStock>> RefreshTradingPlan(int id)
    {
        try
        {
            await _tradingPlanService.UpdateTradingPlanForStockAsync(id, force: true);
            // 重新加载完整的股票数据（包含导航属性）
            var stock = await _context.WatchlistStocks
                .Include(w => w.Stock)
                .Include(w => w.Category)
                .FirstOrDefaultAsync(w => w.Id == id);
            if (stock == null)
            {
                return NotFound("自选股不存在");
            }
            return Ok(stock);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新做T方案失败: {Id}", id);
            return StatusCode(500, new { message = $"刷新做T方案失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// SSE端点：接收做T方案更新通知
    /// </summary>
    [HttpGet("trading-plan/events")]
    public async Task TradingPlanEvents()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var writer = new StreamWriter(Response.Body);
        var clientId = _eventService.AddClient(writer);

        try
        {
            // 发送初始连接确认
            await writer.WriteAsync($"data: {{\"type\":\"connected\",\"clientId\":\"{clientId}\"}}\n\n");
            await writer.FlushAsync();

            // 保持连接，等待客户端断开
            var cancellationToken = HttpContext.RequestAborted;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, cancellationToken); // 每30秒发送一次心跳
                    await _eventService.SendHeartbeatAsync();
                }
                catch (OperationCanceledException)
                {
                    // 客户端断开连接，正常退出
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 客户端断开连接，正常退出，不记录为错误
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSE连接异常: {ClientId}", clientId);
        }
        finally
        {
            _eventService.RemoveClient(clientId);
        }
    }
}

public class AddWatchlistRequest
{
    public string StockCode { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal? Quantity { get; set; }
}

public class UpdateCostRequest
{
    public decimal? CostPrice { get; set; }
    public decimal? Quantity { get; set; }
}

public class UpdateAlertRequest
{
    public decimal? HighAlert { get; set; }
    public decimal? LowAlert { get; set; }
}

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Color { get; set; } = "#1890ff";
}

public class UpdateCategoryRequest
{
    public int CategoryId { get; set; }
}

public class UpdateSuggestedPriceRequest
{
    public decimal? SuggestedBuyPrice { get; set; }
    public decimal? SuggestedSellPrice { get; set; }
}

public class ResetAlertFlagsRequest
{
    public decimal CurrentPrice { get; set; }
}

public class ToggleAutoTradingRequest
{
    public bool Enabled { get; set; }
    public int? IntervalMinutes { get; set; } // 可选，默认30分钟
}

