using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WatchlistController : ControllerBase
{
    private readonly IWatchlistService _watchlistService;
    private readonly ILogger<WatchlistController> _logger;

    public WatchlistController(IWatchlistService watchlistService, ILogger<WatchlistController> logger)
    {
        _watchlistService = watchlistService;
        _logger = logger;
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
        var grouped = await _watchlistService.GetWatchlistGroupedByCategoryAsync();
        // 将所有分类的自选股合并成一个列表
        var allStocks = grouped.Values.SelectMany(stocks => stocks).ToList();
        return Ok(allStocks);
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

