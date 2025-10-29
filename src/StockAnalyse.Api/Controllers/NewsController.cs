using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NewsController : ControllerBase
{
    private readonly INewsService _newsService;
    private readonly ILogger<NewsController> _logger;

    public NewsController(INewsService newsService, ILogger<NewsController> logger)
    {
        _newsService = newsService;
        _logger = logger;
    }

    /// <summary>
    /// 获取最新新闻
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<List<FinancialNews>>> GetLatest(int count = 50)
    {
        var news = await _newsService.GetLatestNewsAsync(count);
        return Ok(news);
    }

    /// <summary>
    /// 获取指定股票的新闻
    /// </summary>
    [HttpGet("stock/{stockCode}")]
    public async Task<ActionResult<List<FinancialNews>>> GetByStock(string stockCode)
    {
        var news = await _newsService.GetNewsByStockAsync(stockCode);
        return Ok(news);
    }

    /// <summary>
    /// 搜索新闻
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<FinancialNews>>> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return BadRequest("关键词不能为空");
        }
        
        var news = await _newsService.SearchNewsAsync(keyword);
        return Ok(news);
    }

    /// <summary>
    /// 手动触发抓取新闻
    /// </summary>
    [HttpPost("fetch")]
    public async Task<ActionResult> FetchNews()
    {
        await _newsService.FetchNewsAsync();
        return Ok(new { message = "新闻抓取任务已启动" });
    }
}

