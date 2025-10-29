using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class NewsService : INewsService
{
    private readonly StockDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<NewsService> _logger;

    public NewsService(StockDbContext context, HttpClient httpClient, ILogger<NewsService> logger)
    {
        _context = context;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<FinancialNews>> GetLatestNewsAsync(int count = 50)
    {
        return await _context.FinancialNews
            .OrderByDescending(n => n.PublishTime)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<FinancialNews>> GetNewsByStockAsync(string stockCode)
    {
        return await _context.FinancialNews
            .Where(n => n.StockCodes != null && n.StockCodes.Contains(stockCode))
            .OrderByDescending(n => n.PublishTime)
            .ToListAsync();
    }

    public async Task FetchNewsAsync()
    {
        try
        {
            // 从财联社抓取新闻（示例）
            await FetchCailianNewsAsync();
            
            // 从新浪财经抓取新闻
            await FetchSinaNewsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "抓取新闻失败");
        }
    }

    public async Task<List<FinancialNews>> SearchNewsAsync(string keyword)
    {
        return await _context.FinancialNews
            .Where(n => n.Title.Contains(keyword) || n.Content.Contains(keyword))
            .OrderByDescending(n => n.PublishTime)
            .ToListAsync();
    }

    private async Task FetchCailianNewsAsync()
    {
        try
        {
            var url = "https://www.cls.cn/api/sw"; // 财联社API（需要替换为实际API）
            
            // 这里只是示例，实际需要根据财联社的API文档实现
            // var response = await _httpClient.GetStringAsync(url);
            // var news = ParseCailianData(response);
            
            // 示例数据
            var news = new List<FinancialNews>
            {
                new FinancialNews
                {
                    Title = "示例新闻标题",
                    Content = "新闻内容...",
                    Source = "财联社",
                    PublishTime = DateTime.Now,
                    FetchTime = DateTime.Now,
                    StockCodes = new List<string> { "000001" }
                }
            };
            
            foreach (var item in news)
            {
                var existing = await _context.FinancialNews
                    .FirstOrDefaultAsync(n => n.Title == item.Title && n.Source == item.Source);
                    
                if (existing == null)
                {
                    await _context.FinancialNews.AddAsync(item);
                }
            }
            
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "抓取财联社新闻失败");
        }
    }

    private async Task FetchSinaNewsAsync()
    {
        try
        {
            // 新浪财经新闻抓取
            _logger.LogInformation("抓取新浪财经新闻");
            
            // 实际实现需要调用新浪财经的API或爬虫
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "抓取新浪财经新闻失败");
        }
    }
}

