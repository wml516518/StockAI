using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using StockAnalyse.Api.Services;
using StockAnalyse.Api.Data;
using System.Text.Json;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NewsController : ControllerBase
{
    private readonly INewsService _newsService;
    private readonly ILogger<NewsController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly StockDbContext _context;
    private readonly IAIService _aiService;

    public NewsController(INewsService newsService, ILogger<NewsController> logger, IServiceProvider serviceProvider, StockDbContext context, IAIService aiService)
    {
        _newsService = newsService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _context = context;
        _aiService = aiService;
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
    /// 获取可用的AI提示词列表
    /// </summary>
    [HttpGet("prompts")]
    public async Task<ActionResult<List<object>>> GetAIPrompts()
    {
        try
        {
            var prompts = await _context.AIPrompts
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description
                })
                .ToListAsync();

            return Ok(prompts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取AI提示词列表失败");
            return StatusCode(500, "获取提示词列表失败：" + ex.Message);
        }
    }

    /// <summary>
    /// AI分析整个页面的新闻 - 综合市场分析
    /// </summary>
    [HttpPost("analyze-batch")]
    public async Task<ActionResult<string>> AnalyzeBatchNews([FromBody] BatchNewsAnalysisRequest request)
    {
        try
        {
            // 验证请求参数
            if (request.NewsIds == null || !request.NewsIds.Any())
            {
                return BadRequest("请提供要分析的新闻ID列表");
            }

            if (request.NewsIds.Count > 50)
            {
                return BadRequest("单次分析的新闻数量不能超过50条");
            }

            // 获取新闻列表
            var newsList = await _context.FinancialNews
                .Where(n => request.NewsIds.Contains(n.Id))
                .OrderByDescending(n => n.PublishTime)
                .ToListAsync();

            if (!newsList.Any())
            {
                return NotFound("未找到指定的新闻");
            }

            // 获取提示词
            string systemPrompt = "你是一名资深的金融新闻分析师。请分析新闻内容对市场的影响，重点关注：1. 新闻涉及的股票和行业；2. 可能对市场的影响；3. 投资机会和风险提示。请给出专业的分析意见。";
            
            if (request.PromptId.HasValue)
            {
                var aiPrompt = await _context.AIPrompts.FindAsync(request.PromptId.Value);
                if (aiPrompt != null && aiPrompt.IsActive)
                {
                    systemPrompt = aiPrompt.SystemPrompt;
                }
            }

            // 构建综合分析提示词
            var newsContent = string.Join("\n\n", newsList.Select((news, index) => 
                $"【新闻{index + 1}】\n" +
                $"标题：{news.Title}\n" +
                $"内容：{(news.Content?.Length > 500 ? news.Content.Substring(0, 500) + "..." : news.Content)}\n" +
                $"来源：{news.Source}\n" +
                $"时间：{news.PublishTime:yyyy-MM-dd HH:mm}\n" +
                $"相关股票：{(news.StockCodes != null && news.StockCodes.Any() ? string.Join(", ", news.StockCodes) : "无")}"
            ));

            // 统计相关股票
            var allStockCodes = newsList
                .Where(n => n.StockCodes != null && n.StockCodes.Any())
                .SelectMany(n => n.StockCodes ?? Enumerable.Empty<string>())
                .GroupBy(code => code)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => $"{g.Key}(出现{g.Count()}次)")
                .ToList();

            var prompt = $@"{systemPrompt}

以下是{newsList.Count}条最新金融新闻：

{newsContent}

【统计信息】
- 新闻总数：{newsList.Count}条
- 时间范围：{newsList.Last().PublishTime:yyyy-MM-dd HH:mm} 至 {newsList.First().PublishTime:yyyy-MM-dd HH:mm}
- 热门股票：{(allStockCodes.Any() ? string.Join(", ", allStockCodes) : "无明确股票")}

请从以下维度进行综合分析：

1. **市场热点总结**
   - 当前市场关注的主要热点和主题
   - 新闻中反映的市场情绪和趋势

2. **行业板块分析**
   - 涉及的主要行业板块
   - 各板块的利好/利空因素

3. **重点股票分析**
   - 新闻中频繁提及的股票及其影响
   - 潜在的投资机会和风险

4. **市场影响评估**
   - 这些新闻对整体市场可能产生的影响
   - 短期和中长期的市场预期

5. **投资建议**
   - 基于当前新闻面的投资策略建议
   - 需要重点关注的风险点

请提供专业、客观的分析意见，避免过于绝对的判断。";

            // 调用AI接口进行分析
            var analysis = await _aiService.ChatAsync(prompt);

            // 记录分析日志
            _logger.LogInformation("批量新闻分析完成，分析了{Count}条新闻", newsList.Count);

            // 返回分析结果
            return Ok(new
            {
                Analysis = analysis,
                NewsCount = newsList.Count,
                TimeRange = new
                {
                    From = newsList.Last().PublishTime,
                    To = newsList.First().PublishTime
                },
                HotStocks = allStockCodes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量AI分析新闻失败");
            return StatusCode(500, "AI分析失败：" + ex.Message);
        }
    }

    /// <summary>
    /// AI分析最新新闻 - 快速市场概览
    /// </summary>
    [HttpPost("analyze-latest")]
    public async Task<ActionResult<string>> AnalyzeLatestNews([FromBody] LatestNewsAnalysisRequest? request = null)
    {
        try
        {
            var count = request?.Count ?? 20;
            var hours = request?.Hours ?? 24;

            // 限制参数范围
            count = Math.Min(Math.Max(count, 5), 50);
            hours = Math.Min(Math.Max(hours, 1), 168); // 最多7天

            // 获取最新新闻
            var cutoffTime = DateTime.Now.AddHours(-hours);
            var latestNews = await _context.FinancialNews
                .Where(n => n.PublishTime >= cutoffTime)
                .OrderByDescending(n => n.PublishTime)
                .Take(count)
                .ToListAsync();

            if (!latestNews.Any())
            {
                return Ok(new
                {
                    Analysis = $"在过去{hours}小时内没有找到新闻数据，无法进行分析。",
                    NewsCount = 0,
                    TimeRange = new { From = cutoffTime, To = DateTime.Now }
                });
            }

            // 使用批量分析逻辑
            var batchRequest = new BatchNewsAnalysisRequest
            {
                NewsIds = latestNews.Select(n => n.Id).ToList()
            };

            return await AnalyzeBatchNews(batchRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "最新新闻AI分析失败");
            return StatusCode(500, "AI分析失败：" + ex.Message);
        }
    }
}

/// <summary>
/// 批量新闻分析请求
/// </summary>
public class BatchNewsAnalysisRequest
{
    /// <summary>
    /// 要分析的新闻ID列表
    /// </summary>
    public List<int> NewsIds { get; set; } = new();
    public int? PromptId { get; set; }
}

/// <summary>
/// 最新新闻分析请求
/// </summary>
public class LatestNewsAnalysisRequest
{
    /// <summary>
    /// 分析的新闻数量，默认20条，最多50条
    /// </summary>
    public int Count { get; set; } = 20;

    /// <summary>
    /// 时间范围（小时），默认24小时，最多168小时（7天）
    /// </summary>
    public int Hours { get; set; } = 24;
}

