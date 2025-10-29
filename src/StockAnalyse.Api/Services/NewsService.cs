using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace StockAnalyse.Api.Services;

public class NewsRefreshSettings
{
    public int IntervalMinutes { get; set; } = 30;
    public bool Enabled { get; set; } = true;
}

public class NewsConfigService
{
    private readonly string _configFilePath;
    private readonly ILogger<NewsConfigService> _logger;

    public NewsConfigService(ILogger<NewsConfigService> logger)
    {
        _logger = logger;
        _configFilePath = Path.Combine(AppContext.BaseDirectory, "news-config.json");
    }

    public async Task<NewsRefreshSettings> GetSettingsAsync()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                return JsonSerializer.Deserialize<NewsRefreshSettings>(json) ?? new NewsRefreshSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取新闻配置失败");
        }
        
        return new NewsRefreshSettings();
    }

    public async Task SaveSettingsAsync(NewsRefreshSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_configFilePath, json);
            _logger.LogInformation("新闻配置已保存");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存新闻配置失败");
        }
    }
}

public class NewsBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NewsBackgroundService> _logger;
    private TimeSpan _refreshInterval;
    private bool _enabled;

    public NewsBackgroundService(IServiceProvider serviceProvider, ILogger<NewsBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // 从配置服务获取初始设置
        UpdateSettingsFromConfigService().Wait();
        
        _logger.LogInformation("新闻定时任务已初始化，刷新间隔: {IntervalMinutes}分钟，启用状态: {Enabled}", 
            _refreshInterval.TotalMinutes, _enabled);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("新闻定时任务开始执行");

        while (!stoppingToken.IsCancellationRequested)
        {
            // 每次循环前检查配置是否变化
            await UpdateSettingsFromConfigService();
            
            if (_enabled)
            {
                try
                {
                    _logger.LogInformation("开始定时刷新金融消息...");
                    
                    using var scope = _serviceProvider.CreateScope();
                    var newsService = scope.ServiceProvider.GetRequiredService<INewsService>();
                    
                    await newsService.FetchNewsAsync();
                    
                    _logger.LogInformation("金融消息定时刷新完成，下次刷新将在 {Interval} 分钟后", _refreshInterval.TotalMinutes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "定时刷新金融消息失败");
                }
            }
            else
            {
                _logger.LogDebug("新闻定时刷新已禁用，等待启用...");
            }

            // 等待指定的时间间隔
            await Task.Delay(_refreshInterval, stoppingToken);
        }
    }
    
    private async Task UpdateSettingsFromConfigService()
    {
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<NewsConfigService>();
        
        var settings = await configService.GetSettingsAsync();
        
        // 只有当设置发生变化时才更新
        if (_refreshInterval.TotalMinutes != settings.IntervalMinutes || _enabled != settings.Enabled)
        {
            _refreshInterval = TimeSpan.FromMinutes(settings.IntervalMinutes);
            _enabled = settings.Enabled;
            
            _logger.LogInformation("新闻定时任务设置已更新: 间隔={IntervalMinutes}分钟, 启用={Enabled}", 
                settings.IntervalMinutes, settings.Enabled);
        }
    }
}

public class NewsService : INewsService{
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
            
            // 尝试使用API抓取
            var apiUrl = "https://finance.sina.com.cn/interface/zt/flashnew/json.php?_=0";
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var jsonContent = await response.Content.ReadAsStringAsync();
            var newsList = ParseSinaNewsJson(jsonContent);
            
            foreach (var news in newsList)
            {
                var existing = await _context.FinancialNews
                    .FirstOrDefaultAsync(n => n.Title == news.Title && n.Source == news.Source);
                    
                if (existing == null)
                {
                    await _context.FinancialNews.AddAsync(news);
                }
            }
            
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API抓取新浪财经新闻失败，尝试备选方案");
            await FetchSinaNewsFallback();
        }
    }

    private List<string> ExtractStockCodesFromTitle(string title)
    {
        var stockCodes = new List<string>();
        
        // 从标题中提取股票代码（如：600000、000001等）
        var pattern = @"\b(6[0-9]{5}|0[0-9]{5}|3[0-9]{5})\b";
        var matches = System.Text.RegularExpressions.Regex.Matches(title, pattern);
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var code = match.Value;
            // 添加市场前缀
            if (code.StartsWith("6"))
                stockCodes.Add($"sh{code}");
            else if (code.StartsWith("0") || code.StartsWith("3"))
                stockCodes.Add($"sz{code}");
        }
        
        return stockCodes;
    }
    
    private List<FinancialNews> ParseSinaNewsJson(string jsonContent)
    {
        var newsList = new List<FinancialNews>();
        
        try
        {
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;
            
            if (root.TryGetProperty("result", out var result) && 
                result.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("title", out var titleElement) &&
                        item.TryGetProperty("url", out var urlElement) &&
                        item.TryGetProperty("ctime", out var timeElement))
                    {
                        var title = titleElement.GetString()?.Trim();
                        var url = urlElement.GetString();
                        var timeStr = timeElement.GetString();
                        
                        if (!string.IsNullOrEmpty(title))
                        {
                            // 解析时间
                            var publishTime = DateTime.Now;
                            if (!string.IsNullOrEmpty(timeStr) && 
                                long.TryParse(timeStr, out var timestamp))
                            {
                                publishTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                            }
                            
                            var news = new FinancialNews
                            {
                                Title = title,
                                Content = $"新浪财经新闻：{title}",
                                Source = "新浪财经",
                                Url = url,
                                PublishTime = publishTime,
                                FetchTime = DateTime.Now,
                                StockCodes = ExtractStockCodesFromTitle(title)
                            };
                            
                            newsList.Add(news);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析新浪财经JSON数据失败");
        }
        
        return newsList;
    }
    
    private async Task FetchSinaNewsFallback()
    {
        try
        {
            _logger.LogInformation("使用备选方案抓取新浪财经新闻");
            
            // 备选方案：使用网页抓取
            var urls = new List<string>
            {
                "https://finance.sina.com.cn/roll/index.d.html?cid=56592", // 财经新闻
                "https://finance.sina.com.cn/roll/index.d.html?cid=56593", // 股票新闻
                "https://finance.sina.com.cn/roll/index.d.html?cid=56594"  // 市场新闻
            };
            
            var newsList = new List<FinancialNews>();
            
            foreach (var url in urls)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    
                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    
                    var htmlContent = await response.Content.ReadAsStringAsync();
                    
                    // 解析HTML获取新闻列表
                    var newsItems = ParseSinaNewsHtml(htmlContent);
                    newsList.AddRange(newsItems);
                    
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "备选方案抓取失败: {Url}", url);
                }
            }
            
            // 保存到数据库
            foreach (var news in newsList)
            {
                var existing = await _context.FinancialNews
                    .FirstOrDefaultAsync(n => n.Title == news.Title && n.Source == news.Source);
                    
                if (existing == null)
                {
                    await _context.FinancialNews.AddAsync(news);
                }
            }
            
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "备选方案抓取新浪财经新闻失败");
        }
    }
    
    private List<FinancialNews> ParseSinaNewsHtml(string htmlContent)
    {
        var newsList = new List<FinancialNews>();
        
        try
        {
            // 简单的HTML解析，实际生产环境建议使用HtmlAgilityPack等专业库
            // 这里使用正则表达式提取新闻标题和链接
            var titlePattern = @"<a href=""(http[^""]+)""[^>]*>([^<]+)</a>";
            var timePattern = @"<span class=""time"">([^<]+)</span>";
            
            var titleMatches = System.Text.RegularExpressions.Regex.Matches(htmlContent, titlePattern);
            var timeMatches = System.Text.RegularExpressions.Regex.Matches(htmlContent, timePattern);
            
            for (int i = 0; i < Math.Min(titleMatches.Count, 10); i++) // 限制数量避免过多
            {
                var match = titleMatches[i];
                if (match.Groups.Count >= 3)
                {
                    var url = match.Groups[1].Value;
                    var title = match.Groups[2].Value.Trim();
                    
                    // 尝试获取发布时间
                    var publishTime = DateTime.Now;
                    if (i < timeMatches.Count)
                    {
                        var timeMatch = timeMatches[i];
                        if (timeMatch.Groups.Count >= 2)
                        {
                            var timeStr = timeMatch.Groups[1].Value.Trim();
                            if (DateTime.TryParse(timeStr, out var parsedTime))
                            {
                                publishTime = parsedTime;
                            }
                        }
                    }
                    
                    var news = new FinancialNews
                    {
                        Title = title,
                        Content = $"新浪财经新闻：{title}", // 简化内容，实际可以抓取详情页
                        Source = "新浪财经",
                        Url = url,
                        PublishTime = publishTime,
                        FetchTime = DateTime.Now,
                        StockCodes = ExtractStockCodesFromTitle(title)
                    };
                    
                    newsList.Add(news);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析新浪财经HTML失败");
        }
        
        return newsList;
    }
}

