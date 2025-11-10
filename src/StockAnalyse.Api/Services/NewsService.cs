using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;

namespace StockAnalyse.Api.Services;

public class NewsService : INewsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NewsService> _logger;

    public NewsService(HttpClient httpClient, ILogger<NewsService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
    }

    public async Task<List<FinancialNews>> GetNewsByStockAsync(string stockCode)
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var response = await _httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("Python服务获取个股新闻失败: 状态码={StatusCode}, 错误={Error}", response.StatusCode, errorContent);
                return new List<FinancialNews>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

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
}

