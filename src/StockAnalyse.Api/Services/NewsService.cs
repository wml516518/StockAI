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
    public bool EnableTianApiNews { get; set; } = true;  // 是否启用天行数据新闻
    public int TianApiNewsInterval { get; set; } = 15;   // 天行数据新闻刷新间隔（分钟）
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
        
        // 记录上次天行数据新闻刷新时间
        DateTime lastTianApiNewsRefreshTime = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            // 每次循环前检查配置是否变化
            await UpdateSettingsFromConfigService();
            
            if (_enabled)
            {
                try
                {
                    var settings = await GetCurrentSettings();
                    var now = DateTime.Now;
                    bool shouldRefreshTianApi = settings.EnableTianApiNews && 
                        (now - lastTianApiNewsRefreshTime).TotalMinutes >= settings.TianApiNewsInterval;
                    
                    if (shouldRefreshTianApi)
                    {
                        _logger.LogInformation("开始定时刷新天行数据财经新闻...");
                        
                        using var scope = _serviceProvider.CreateScope();
                        var newsService = scope.ServiceProvider.GetRequiredService<INewsService>();
                        
                        // 只调用天行数据API
                        await ((NewsService)newsService).FetchTianApiNewsOnlyAsync();
                        
                        lastTianApiNewsRefreshTime = now;
                        _logger.LogInformation("天行数据财经新闻定时刷新完成，下次刷新将在 {Interval} 分钟后", 
                            settings.TianApiNewsInterval);
                    }
                    
                    // 常规新闻刷新
                    _logger.LogInformation("开始定时刷新所有金融消息...");
                    
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var newsService = scope.ServiceProvider.GetRequiredService<INewsService>();
                        await newsService.FetchNewsAsync();
                    }
                    
                    _logger.LogInformation("所有金融消息定时刷新完成，下次刷新将在 {Interval} 分钟后", _refreshInterval.TotalMinutes);
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
    
    private async Task<NewsRefreshSettings> GetCurrentSettings()
    {
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<NewsConfigService>();
        return await configService.GetSettingsAsync();
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
            _logger.LogInformation("开始抓取金融消息");
            
            // 从天行数据抓取财经新闻
            await FetchTianApiNewsAsync();
            
            // 从财联社抓取新闻（示例）
            await FetchCailianNewsAsync();
            
            // 从新浪财经抓取新闻
            await FetchSinaNewsAsync();
            
            _logger.LogInformation("金融消息抓取完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "抓取新闻失败");
        }
    }
    
    // 只从天行数据抓取财经新闻（用于定时任务）
    public async Task FetchTianApiNewsOnlyAsync()
    {
        try
        {
            _logger.LogInformation("开始单独抓取天行数据财经新闻");
            await FetchTianApiNewsAsync();
            _logger.LogInformation("天行数据财经新闻抓取完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "单独抓取天行数据财经新闻失败: {Message}", ex.Message);
        }
    }
    
    // 保存新闻到数据库
    private async Task SaveNewsToDatabase(List<FinancialNews> newsList)
    {
        int addedCount = 0;
        foreach (var news in newsList)
        {
            var existing = await _context.FinancialNews
                .FirstOrDefaultAsync(n => n.Title == news.Title && n.Source == news.Source);
                
            if (existing == null)
            {
                await _context.FinancialNews.AddAsync(news);
                addedCount++;
            }
        }
        
        if (addedCount > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("成功保存 {Count} 条新闻到数据库", addedCount);
        }
        else
        {
            _logger.LogInformation("没有新的新闻需要保存到数据库");
        }
    }
    
    // 从天行数据抓取财经新闻
    private async Task FetchTianApiNewsAsync()
    {
        try
        {
            _logger.LogInformation("从天行数据抓取财经新闻");
            
            // 天行数据API接口地址和密钥（使用已申请的key）
            var apiUrl = "https://apis.tianapi.com/caijing/index";
            var apiKey = "267b24bc0090305f6dcc6634e4e17fd4"; // 更新为新的key
            
            // 构建请求参数
            var requestUrl = $"{apiUrl}?key={apiKey}&num=50";
            
            _logger.LogInformation("正在请求天行数据API: {Url}", requestUrl);
            
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            // 设置30秒超时
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            var response = await _httpClient.SendAsync(request);
            
            // 记录响应状态码
            _logger.LogInformation("天行数据API响应状态码: {StatusCode}", response.StatusCode);
            
            // 不抛出异常，而是记录错误响应
            var jsonContent = await response.Content.ReadAsStringAsync();
            
            // 记录响应内容长度和预览
            _logger.LogInformation("天行数据API响应内容长度: {Length}字节", jsonContent?.Length ?? 0);
            if (!string.IsNullOrEmpty(jsonContent) && jsonContent.Length > 0)
            {
                var previewLength = Math.Min(jsonContent.Length, 100);
                _logger.LogInformation("天行数据API响应预览: {Preview}...", jsonContent.Substring(0, previewLength));
            }
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("天行数据API请求失败: {StatusCode}, 错误内容: {Content}", response.StatusCode, jsonContent);
                return;
            }
            
            var newsList = ParseTianApiNewsJson(jsonContent);
            
            _logger.LogInformation("从天行数据获取到 {Count} 条财经新闻", newsList.Count);
            
            await SaveNewsToDatabase(newsList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从天行数据抓取财经新闻失败");
        }
    }
    
    // 解析天行数据API返回的JSON
    private List<FinancialNews> ParseTianApiNewsJson(string jsonContent)
    {
        var newsList = new List<FinancialNews>();
        
        try
        {
            _logger.LogInformation("开始解析天行数据JSON: {Length}字节", jsonContent?.Length ?? 0);
            
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;
            
            // 记录API返回的状态码
            if (root.TryGetProperty("code", out var codeElement))
            {
                _logger.LogInformation("天行数据API返回状态码: {Code}", codeElement.GetInt32());
            }
            
            // 记录API返回的消息
            if (root.TryGetProperty("msg", out var msgElement))
            {
                _logger.LogInformation("天行数据API返回消息: {Message}", msgElement.GetString());
            }
            
            if (root.TryGetProperty("code", out var code) && 
                code.GetInt32() == 200 &&
                root.TryGetProperty("result", out var result) && 
                result.TryGetProperty("list", out var list))
            {
                _logger.LogInformation("成功获取天行数据新闻列表");
                
                foreach (var item in list.EnumerateArray())
                {
                    try
                    {
                        string title = "";
                        string url = "";
                        string timeStr = "";
                        string source = "天行数据";
                        string description = "";
                        
                        if (item.TryGetProperty("title", out var titleElement))
                            title = titleElement.GetString()?.Trim() ?? "";
                            
                        if (item.TryGetProperty("url", out var urlElement))
                            url = urlElement.GetString() ?? "";
                            
                        if (item.TryGetProperty("ctime", out var timeElement))
                            timeStr = timeElement.GetString() ?? "";
                            
                        if (item.TryGetProperty("source", out var sourceElement))
                            source = sourceElement.GetString() ?? "天行数据";
                            
                        if (item.TryGetProperty("description", out var descElement))
                            description = descElement.GetString() ?? "";
                        
                        if (!string.IsNullOrEmpty(title))
                        {
                            // 解析时间
                            var publishTime = DateTime.Now;
                            if (!string.IsNullOrEmpty(timeStr))
                            {
                                try
                                {
                                    publishTime = DateTime.Parse(timeStr);
                                }
                                catch
                                {
                                    _logger.LogWarning("无法解析时间: {TimeStr}", timeStr);
                                }
                            }
                            
                            var content = !string.IsNullOrEmpty(description) ? description : title;
                            
                            var news = new FinancialNews
                            {
                                Title = title,
                                Content = content,
                                Source = source,
                                Url = url,
                                PublishTime = publishTime,
                                FetchTime = DateTime.Now,
                                StockCodes = ExtractStockCodesFromTitle(title)
                            };
                            
                            newsList.Add(news);
                        }
                    }
                    catch (Exception itemEx)
                    {
                        _logger.LogError(itemEx, "处理单条天行数据新闻时出错");
                    }
                }
                
                _logger.LogInformation("成功解析 {Count} 条天行数据新闻", newsList.Count);
            }
            else
            {
                _logger.LogWarning("天行数据API返回格式不符合预期");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析天行数据JSON数据失败");
        }
        
        return newsList;
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

