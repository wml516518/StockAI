using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
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
                        
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var newsService = scope.ServiceProvider.GetRequiredService<INewsService>();
                            
                            // 只调用天行数据API
                            await ((NewsService)newsService).FetchTianApiNewsOnlyAsync();
                        }
                        
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
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private const int CacheExpirationMinutes = 30; // 缓存30分钟

    public NewsService(StockDbContext context, HttpClient httpClient, ILogger<NewsService> logger, IMemoryCache cache, IServiceScopeFactory serviceScopeFactory)
    {
        _context = context;
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <summary>
    /// 直接从外部API获取新闻（不保存到数据库，带缓存）
    /// </summary>
    private async Task<List<FinancialNews>> GetNewsFromApiAsync(int count = 50)
    {
        // 缓存键：使用固定的键名，因为新闻数据对所有用户都是一样的
        var cacheKey = "LatestFinancialNews";
        
        // 尝试从缓存获取
        if (_cache.TryGetValue(cacheKey, out List<FinancialNews>? cachedNews))
        {
            _logger.LogInformation("从缓存获取新闻，数量: {Count}", cachedNews?.Count ?? 0);
            
            // 如果缓存的数据数量足够，直接返回
            if (cachedNews != null && cachedNews.Count >= count)
            {
                var result = cachedNews.Take(count).ToList();
                _logger.LogInformation("从缓存返回新闻: 请求数量={RequestCount}, 返回数量={ResultCount}, 缓存总量={CacheTotal}", 
                    count, result.Count, cachedNews.Count);
                // 记录前3条新闻的标题用于调试
                if (result.Count > 0)
                {
                    var previewTitles = string.Join(", ", result.Take(3).Select(n => n.Title ?? "无标题"));
                    _logger.LogInformation("缓存新闻预览（前3条）: {Titles}", previewTitles);
                }
                return result;
            }
            // 如果缓存的数据数量不够，但仍然使用缓存的数据（避免重复请求）
            else if (cachedNews != null && cachedNews.Count > 0)
            {
                _logger.LogInformation("缓存数据不足，返回所有缓存: 请求数量={RequestCount}, 缓存数量={CacheCount}", 
                    count, cachedNews.Count);
                return cachedNews;
            }
        }
        
        // 缓存未命中或已过期，从API获取
        try
        {
            _logger.LogInformation("从外部API获取最新财经新闻，数量: {Count}", count);
            
            // 天行数据API接口地址和密钥
            var apiUrl = "https://apis.tianapi.com/caijing/index";
            var apiKey = "267b24bc0090305f6dcc6634e4e17fd4";
            
            // 构建请求参数，获取更多数据以支持缓存
            // 天行数据API最多支持返回50条，所以限制在50条
            // 为了支持分页，总是请求最大数量50条
            var requestCount = 50;
            _logger.LogInformation("API请求数量: 请求={RequestCount}, 实际请求={ActualCount}（天行API限制为50条）", count, requestCount);
            var requestUrl = $"{apiUrl}?key={apiKey}&num={requestCount}";
            
            _logger.LogInformation("正在请求天行数据API: {Url}", requestUrl);
            
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            // 设置30秒超时
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("天行数据API请求失败: {StatusCode}", response.StatusCode);
                // 如果API失败但有缓存数据，返回缓存数据
                if (cachedNews != null && cachedNews.Count > 0)
                {
                    _logger.LogInformation("API请求失败，返回缓存数据");
                    return cachedNews.Take(count).ToList();
                }
                return new List<FinancialNews>();
            }
            
            var jsonContent = await response.Content.ReadAsStringAsync();
            
            if (string.IsNullOrEmpty(jsonContent))
            {
                _logger.LogWarning("天行数据API返回内容为空");
                // 如果API返回空但有缓存数据，返回缓存数据
                if (cachedNews != null && cachedNews.Count > 0)
                {
                    _logger.LogInformation("API返回空，返回缓存数据");
                    return cachedNews.Take(count).ToList();
                }
                return new List<FinancialNews>();
            }
            
            // 记录API响应的前200个字符用于调试
            var preview = jsonContent.Length > 200 ? jsonContent.Substring(0, 200) : jsonContent;
            _logger.LogInformation("API响应内容预览: {Preview}", preview);
            
            var newsList = ParseTianApiNewsJson(jsonContent);
            
            if (newsList == null || newsList.Count == 0)
            {
                _logger.LogWarning("解析后的新闻列表为空，API响应: {JsonContent}", jsonContent);
                // 如果解析失败但有缓存数据，返回缓存数据
                if (cachedNews != null && cachedNews.Count > 0)
                {
                    _logger.LogInformation("解析失败，返回缓存数据");
                    return cachedNews.Take(count).ToList();
                }
                return new List<FinancialNews>();
            }
            
            // 按发布时间倒序排序
            newsList = newsList
                .OrderByDescending(n => n.PublishTime)
                .ToList();
            
            _logger.LogInformation("从外部API成功获取 {Count} 条财经新闻", newsList.Count);
            
            // 记录新闻详情用于调试
            if (newsList.Count > 0)
            {
                _logger.LogInformation("新闻列表详情:");
                for (int i = 0; i < Math.Min(newsList.Count, 5); i++)
                {
                    var news = newsList[i];
                    _logger.LogInformation("  [{Index}] 标题: {Title}, 发布时间: {PublishTime}, 来源: {Source}, URL: {Url}", 
                        i + 1, 
                        news.Title ?? "无标题", 
                        news.PublishTime, 
                        news.Source ?? "未知", 
                        news.Url ?? "无链接");
                }
                if (newsList.Count > 5)
                {
                    _logger.LogInformation("  ... 还有 {MoreCount} 条新闻", newsList.Count - 5);
                }
            }
            else
            {
                _logger.LogWarning("⚠️ 注意：解析后的新闻列表为空！");
            }
            
            // 将数据存入缓存，设置30分钟过期时间
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
                SlidingExpiration = null, // 不使用滑动过期
                Priority = CacheItemPriority.Normal
            };
            
            _cache.Set(cacheKey, newsList, cacheOptions);
            _logger.LogInformation("新闻数据已缓存，将在 {Minutes} 分钟后过期", CacheExpirationMinutes);
            
            // 返回请求的数量
            var returnList = newsList.Take(count).ToList();
            _logger.LogInformation("准备返回新闻: 请求数量={RequestCount}, 实际返回={ReturnCount}", count, returnList.Count);
            return returnList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从外部API获取新闻失败");
            // API异常时，再次尝试从缓存获取（可能其他并发请求已经缓存了数据）
            if (_cache.TryGetValue(cacheKey, out List<FinancialNews>? fallbackNews))
            {
                if (fallbackNews != null && fallbackNews.Count > 0)
                {
                    _logger.LogInformation("API异常，返回缓存数据作为降级方案");
                    return fallbackNews.Take(count).ToList();
                }
            }
            // 如果之前有缓存数据，也尝试使用
            if (cachedNews != null && cachedNews.Count > 0)
            {
                _logger.LogInformation("API异常，返回之前的缓存数据");
                return cachedNews.Take(count).ToList();
            }
            return new List<FinancialNews>();
        }
    }

    public async Task<List<FinancialNews>> GetLatestNewsAsync(int count = 50)
    {
        // 直接从外部API获取，不保存到数据库
        return await GetNewsFromApiAsync(count);
    }

    public async Task<PagedResult<FinancialNews>> GetLatestNewsPagedAsync(int pageIndex = 1, int pageSize = 20)
    {
        // 立即输出到控制台，确保能看到
        _logger.LogDebug("GetLatestNewsPagedAsync 开始: PageIndex={PageIndex}, PageSize={PageSize}", pageIndex, pageSize);
        
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Max(1, Math.Min(100, pageSize)); // 限制每页最多100条

        _logger.LogInformation("============================================");
        _logger.LogInformation("📰 [NewsService] GetLatestNewsPagedAsync 开始");
        _logger.LogInformation("📰 [NewsService] 参数: PageIndex={PageIndex}, PageSize={PageSize}", pageIndex, pageSize);
        _logger.LogInformation("============================================");

        // 从外部API获取数据以支持分页
        // 注意：天行数据API最多只返回50条新闻，这是外部API的限制
        // 所以我们最多只能获取50条，然后在这50条中进行分页
        var requestedCount = 50; // 固定请求50条（天行API的最大值）
        _logger.LogInformation("📰 [NewsService] 准备从API获取 {RequestedCount} 条新闻（天行API限制为50条，将在这50条中进行分页）", requestedCount);
        
        var allNews = await GetNewsFromApiAsync(requestedCount);
        
        _logger.LogInformation("📰 [NewsService] 实际获取到 {ActualCount} 条新闻", allNews.Count);
        
        var totalCount = allNews.Count;
        var items = allNews
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        _logger.LogInformation("📰 [NewsService] 分页结果: TotalCount={TotalCount}, ItemsCount={ItemsCount}, PageIndex={PageIndex}, PageSize={PageSize}", 
            totalCount, items.Count, pageIndex, pageSize);

        return new PagedResult<FinancialNews>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<List<FinancialNews>> GetNewsByStockAsync(string stockCode)
    {
        // 从外部API获取新闻，然后在内存中过滤股票代码
        var allNews = await GetNewsFromApiAsync(100);
        
        return allNews
            .Where(n => n.StockCodes != null && n.StockCodes.Contains(stockCode))
            .OrderByDescending(n => n.PublishTime)
            .ToList();
    }

    public async Task FetchNewsAsync()
    {
        try
        {
            _logger.LogInformation("开始抓取金融消息");
            
            // 仅使用天行数据
            await FetchTianApiNewsAsync();
            
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
        // 创建一个新的作用域来确保 DbContext 在整个操作期间有效
        using (var scope = _serviceScopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StockDbContext>();
            
            int addedCount = 0;
            foreach (var news in newsList)
            {
                var existing = await context.FinancialNews
                    .FirstOrDefaultAsync(n => n.Title == news.Title && n.Source == news.Source);
                    
                if (existing == null)
                {
                    await context.FinancialNews.AddAsync(news);
                    addedCount++;
                }
            }
            
            if (addedCount > 0)
            {
                await context.SaveChangesAsync();
                _logger.LogInformation("成功保存 {Count} 条新闻到数据库", addedCount);
            }
            else
            {
                _logger.LogInformation("没有新的新闻需要保存到数据库");
            }
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
                _logger.LogError("天行数据API请求失败: {StatusCode}, 错误内容: {Content}", response.StatusCode, jsonContent ?? "null");
                return;
            }
            
            if (string.IsNullOrEmpty(jsonContent))
            {
                _logger.LogWarning("天行数据API返回内容为空");
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
        
        if (string.IsNullOrEmpty(jsonContent))
        {
            _logger.LogWarning("尝试解析空的JSON内容");
            return newsList;
        }
        
        try
        {
            _logger.LogInformation("开始解析天行数据JSON: {Length}字节", jsonContent.Length);
            
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;
            
            // 记录API返回的状态码
            int? apiCode = null;
            if (root.TryGetProperty("code", out var codeElement))
            {
                apiCode = codeElement.GetInt32();
                _logger.LogInformation("天行数据API返回状态码: {Code}", apiCode);
            }
            
            // 记录API返回的消息
            if (root.TryGetProperty("msg", out var msgElement))
            {
                var msg = msgElement.GetString();
                _logger.LogInformation("天行数据API返回消息: {Message}", msg);
                
                // 如果返回错误消息，记录并返回空列表
                if (apiCode.HasValue && apiCode.Value != 200)
                {
                    _logger.LogError("天行数据API返回错误: Code={Code}, Message={Message}", apiCode.Value, msg);
                    return newsList;
                }
            }
            
            if (root.TryGetProperty("code", out var code) && 
                code.GetInt32() == 200 &&
                root.TryGetProperty("result", out var result))
            {
                _logger.LogInformation("成功获取天行数据新闻列表");
                
                // 检查是否有newslist属性
                if (result.TryGetProperty("newslist", out var newslist))
                {
                    foreach (var item in newslist.EnumerateArray())
                    {
                        try
                        {
                            string title = "";
                            string url = "";
                            string timeStr = "";
                            string source = "天行数据";
                            string description = "";
                            string content = "";
                            
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
                                
                            // 尝试获取详细内容
                            if (item.TryGetProperty("content", out var contentElement))
                                content = contentElement.GetString() ?? "";
                            
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
                                
                                // 优先使用content，其次是description，最后是title
                                var finalContent = !string.IsNullOrEmpty(content) ? content : 
                                                  (!string.IsNullOrEmpty(description) ? description : title);
                                
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
                }
                // 尝试旧的格式（list属性）
                else if (result.TryGetProperty("list", out var list))
                {
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
                }
                
                _logger.LogInformation("✅ 成功解析 {Count} 条天行数据新闻", newsList.Count);
                
                // 记录解析结果的详细信息
                if (newsList.Count > 0)
                {
                    _logger.LogInformation("📰 解析结果详情（前5条）:");
                    for (int i = 0; i < Math.Min(newsList.Count, 5); i++)
                    {
                        var news = newsList[i];
                        _logger.LogInformation("  [{Index}] 标题: {Title}, 发布时间: {PublishTime}, 来源: {Source}, URL: {Url}", 
                            i + 1, news.Title ?? "无标题", news.PublishTime, news.Source ?? "未知", news.Url ?? "无链接");
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ 解析后新闻列表为空，但API返回code=200");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ 天行数据API返回格式不符合预期。Code={Code}, HasResult={HasResult}", 
                    apiCode?.ToString() ?? "未知", 
                    root.TryGetProperty("result", out _));
                
                // 尝试输出完整的JSON结构以便调试
                try
                {
                    var jsonPreview = jsonContent.Length > 500 ? jsonContent.Substring(0, 500) + "..." : jsonContent;
                    _logger.LogWarning("JSON响应预览: {JsonPreview}", jsonPreview);
                }
                catch
                {
                    // 忽略日志输出错误
                }
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
        // 从外部API获取新闻，然后在内存中搜索
        var allNews = await GetNewsFromApiAsync(100); // 获取更多新闻以支持搜索
        
        return allNews
            .Where(n => 
                (n.Title != null && n.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (n.Content != null && n.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.PublishTime)
            .ToList();
    }

    public async Task<PagedResult<FinancialNews>> SearchNewsPagedAsync(string keyword, int pageIndex = 1, int pageSize = 20)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Max(1, Math.Min(100, pageSize)); // 限制每页最多100条

        // 从外部API获取新闻，然后在内存中搜索和分页
        var allNews = await GetNewsFromApiAsync(200); // 获取更多新闻以支持搜索和分页
        
        var filteredNews = allNews
            .Where(n => 
                (n.Title != null && n.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (n.Content != null && n.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.PublishTime)
            .ToList();
        
        var totalCount = filteredNews.Count;
        var items = filteredNews
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<FinancialNews>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }



    private async Task FetchCailianNewsAsync()
    {
        try
        {
            // 财联社API（需要替换为实际API）
            // const string url = "https://www.cls.cn/api/sw";
            
            // 这里只是示例，实际需要根据财联社的API文档实现
            // var response = await _httpClient.GetStringAsync(url);
            // var news = ParseCailianData(response);
            
            // 实际从API获取数据，这里暂时不添加示例数据
            var news = new List<FinancialNews>();
            
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

