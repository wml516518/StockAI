using System.Linq;
using System.Threading;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;

namespace StockAnalyse.Api.Services;

public class NewsService : INewsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NewsService> _logger;
    private readonly IStockDataCacheService _cacheService;

    private const string NewsCacheType = "news";
    private static readonly TimeSpan NewsCacheDuration = TimeSpan.FromMinutes(30);

    public NewsService(HttpClient httpClient, ILogger<NewsService> logger, IStockDataCacheService cacheService)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
        _cacheService = cacheService;
    }

    public async Task<List<FinancialNews>> GetNewsByStockAsync(string stockCode, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            return new List<FinancialNews>();
        }

        stockCode = stockCode.Trim().ToUpperInvariant();

        if (!forceRefresh)
        {
            var cached = await _cacheService.TryGetAsync<List<FinancialNews>>(stockCode, NewsCacheType, allowExpired: false, cancellationToken);
            if (cached != null && cached.Count > 0)
            {
                _logger.LogInformation("使用数据库缓存的新闻数据: 股票 {StockCode}, 条数 {Count}", stockCode, cached.Count);
                return cached;
            }
        }
        else
        {
            _logger.LogInformation("强制刷新股票 {StockCode} 的新闻缓存", stockCode);
        }

        List<FinancialNews> freshNews = new();
        try
        {
            freshNews = await FetchNewsFromSourcesAsync(stockCode, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取股票 {StockCode} 新闻时发生异常，将尝试使用缓存", stockCode);
        }

        if (freshNews.Count > 0)
        {
            await CacheNewsAsync(stockCode, freshNews, isFallback: false, cancellationToken: cancellationToken);
            return freshNews;
        }

        var staleNews = await _cacheService.TryGetAsync<List<FinancialNews>>(stockCode, NewsCacheType, allowExpired: true, cancellationToken);
        if (staleNews != null && staleNews.Count > 0)
        {
            _logger.LogWarning("使用过期的新闻缓存数据: 股票 {StockCode}, 条数 {Count}", stockCode, staleNews.Count);
            await CacheNewsAsync(stockCode, staleNews, isFallback: true, ttlOverride: TimeSpan.FromMinutes(10), cancellationToken: cancellationToken);
            return staleNews;
        }

        return freshNews;
    }

    private async Task<List<FinancialNews>> FetchNewsFromSourcesAsync(string stockCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            return new List<FinancialNews>();
        }

        try
        {
            var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL")
                ?? "http://localhost:5001";

            var normalizedCode = stockCode.Trim();
            var url = $"{pythonServiceUrl}/api/news/stock/{normalizedCode}";

            _logger.LogInformation("从Python服务(AKShare)获取个股新闻: {Url}", url);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"))
            {
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            }
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var response = await _httpClient.SendAsync(request, linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(linkedCts.Token);
                _logger.LogWarning("Python服务获取个股新闻失败: 状态码={StatusCode}, 错误={Error}", response.StatusCode, errorContent);
                return new List<FinancialNews>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: linkedCts.Token);

            var root = document.RootElement;
            if (!root.TryGetProperty("success", out var successElement) || !successElement.GetBoolean())
            {
                _logger.LogWarning("Python服务返回的success不为true: {Json}", root.ToString());
                return new List<FinancialNews>();
            }

            if (!root.TryGetProperty("data", out var dataElement))
            {
                _logger.LogWarning("Python服务返回的数据中缺少data字段");
                return new List<FinancialNews>();
            }

            if (!dataElement.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Python服务返回的数据中缺少items数组");
                return new List<FinancialNews>();
            }

            var result = new List<(int index, FinancialNews news)>();
            var position = 0;

            foreach (var item in itemsElement.EnumerateArray())
            {
                static string? GetString(JsonElement element, string propertyName)
                {
                    if (element.TryGetProperty(propertyName, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
                    {
                        var value = valueElement.GetString();
                        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    }

                    return null;
                }

                var publishTime = DateTime.Now;
                var publishRaw = GetString(item, "publishTime");
                if (!string.IsNullOrWhiteSpace(publishRaw))
                {
                    if (DateTimeOffset.TryParse(publishRaw, out var offsetTime))
                    {
                        publishTime = offsetTime.LocalDateTime;
                    }
                    else if (DateTime.TryParse(publishRaw, out var parsedTime))
                    {
                        publishTime = parsedTime;
                    }
                }

                var news = new FinancialNews
                {
                    Title = GetString(item, "title") ?? string.Empty,
                    Content = GetString(item, "content") ?? string.Empty,
                    Source = GetString(item, "source") ?? "AKShare",
                    Url = GetString(item, "url"),
                    PublishTime = publishTime,
                    StockCodes = new List<string> { stockCode },
                    FetchTime = DateTime.Now,
                    Keywords = GetString(item, "keywords"),
                    Summary = GetString(item, "summary")
                };

                if (string.IsNullOrWhiteSpace(news.Summary))
                {
                    news.Summary = news.Content;
                }

                if (string.IsNullOrWhiteSpace(news.Content) && !string.IsNullOrWhiteSpace(news.Summary))
                {
                    news.Content = news.Summary;
                }

                result.Add((position, news));
                position++;
            }

            return result
                .OrderBy(tuple => tuple.index)
                .Select(tuple => tuple.news)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从Python服务获取个股新闻失败: {StockCode}", stockCode);
            return new List<FinancialNews>();
        }
    }

    private async Task CacheNewsAsync(string stockCode, List<FinancialNews> news, bool isFallback, TimeSpan? ttlOverride = null, CancellationToken cancellationToken = default)
    {
        if (news.Count == 0)
        {
            return;
        }

        var ttl = ttlOverride ?? NewsCacheDuration;
        var metadataObj = new
        {
            cachedAtUtc = DateTime.UtcNow,
            count = news.Count,
            earliestPublishTime = news.Min(n => n.PublishTime),
            latestPublishTime = news.Max(n => n.PublishTime),
            isFallback
        };

        string? metadata = null;
        try
        {
            metadata = JsonSerializer.Serialize(metadataObj);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "序列化新闻缓存元数据失败，将忽略元数据: {StockCode}", stockCode);
        }

        try
        {
            await _cacheService.CacheAsync(stockCode, NewsCacheType, news, ttl, isFallback, metadata, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入新闻缓存失败: {StockCode}", stockCode);
        }
    }
}

