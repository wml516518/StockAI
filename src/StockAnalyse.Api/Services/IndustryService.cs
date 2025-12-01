using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class IndustryService : IIndustryService
{
    private readonly ILogger<IndustryService> _logger;
    private readonly HttpClient _httpClient;

    public IndustryService(ILogger<IndustryService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<IndustryInfoResult?> GetIndustryInfoFromAKShareAsync(string stockCode)
    {
        try
        {
            var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                ?? "http://localhost:5001";
            
            var normalizedStockCode = (stockCode ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalizedStockCode))
            {
                return null;
            }

            var encodedStockCode = Uri.EscapeDataString(normalizedStockCode);
            var url = $"{pythonServiceUrl}/api/stock/industry/{encodedStockCode}";
            
            _logger.LogDebug("尝试从Python服务获取行业数据: {Url}", url);
            
            // 使用短暂的超时，避免阻塞太久
            using var pythonClient = new HttpClient();
            pythonClient.Timeout = TimeSpan.FromSeconds(120); // 增加超时时间到120秒，因为AKShare有时候比较慢
            pythonClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            var response = await pythonClient.GetAsync(url);
            
            // 如果返回404，说明数据未找到，返回null
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Python服务(AKShare)无法获取行业数据");
                return null;
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Python服务返回错误状态码: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }
            
            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonData = JObject.Parse(responseContent);
            
            if (jsonData["success"]?.ToObject<bool>() == true)
            {
                var data = jsonData["data"] as JObject;
                if (data != null)
                {
                    var industryName = data["industry"]?.ToString() ?? "未知";
                    var industryCode = data["industryCode"]?.ToString() ?? string.Empty;
                    var industryDescription = data["description"]?.ToString() ?? string.Empty;
                    var industryStocks = data["stocks"] as JArray;
                    var industryTrends = data["trends"]?.ToString() ?? string.Empty;
                    var industryPerformance = data["performance"] as JObject;
                    var industryMarketData = data["marketData"] as JObject;

                    var builder = new StringBuilder();
                    builder.AppendLine();
                    builder.AppendLine("【行业详情】（数据来源：AKShare - stock_board_industry_name_em）");
                    builder.AppendLine();
                    builder.AppendLine("**行业基本信息：**");
                    builder.AppendLine($"- 行业名称：{industryName}");
                    builder.AppendLine($"- 行业代码：{industryCode}");
                    if (!string.IsNullOrEmpty(industryDescription))
                    {
                        builder.AppendLine($"- 行业描述：{industryDescription}");
                    }
                    builder.AppendLine();

                    var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    static string? NormalizeKeyword(string? value)
                    {
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            return null;
                        }

                        var normalized = value.Replace("（", "(").Replace("）", ")");
                        var index = normalized.IndexOf('(');
                        if (index > 0)
                        {
                            normalized = normalized[..index];
                        }

                        normalized = normalized.Trim();
                        return normalized.Length >= 2 ? normalized : null;
                    }

                    void AddKeyword(string? value)
                    {
                        var normalized = NormalizeKeyword(value);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            keywords.Add(normalized);
                        }
                    }

                    void AddSplitKeywords(string? value)
                    {
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            return;
                        }

                        var separators = new[] { '/', '、', '-', '，', ',', ' ' };
                        foreach (var token in value.Split(separators, StringSplitOptions.RemoveEmptyEntries))
                        {
                            AddKeyword(token);
                        }
                    }

                    if (!string.Equals(industryName, "未知", StringComparison.OrdinalIgnoreCase))
                    {
                        AddKeyword(industryName);
                        AddSplitKeywords(industryName);
                    }

                    AddKeyword(industryCode);

                    if (industryMarketData != null && industryMarketData.Count > 0)
                    {
                        builder.AppendLine("**行业板块实时市场数据：**");

                        var latestPrice = industryMarketData["latestPrice"]?.ToString();
                        var changeAmount = industryMarketData["changeAmount"]?.ToString();
                        var changePercent = industryMarketData["changePercent"]?.ToString();
                        var totalMarketCap = industryMarketData["totalMarketCap"]?.ToString();
                        var turnoverRate = industryMarketData["turnoverRate"]?.ToString();
                        var risingCount = industryMarketData["risingCount"]?.ToString();
                        var fallingCount = industryMarketData["fallingCount"]?.ToString();
                        var leaderStock = industryMarketData["leaderStock"]?.ToString();
                        var leaderChangePercent = industryMarketData["leaderChangePercent"]?.ToString();

                        if (!string.IsNullOrEmpty(latestPrice) && latestPrice != "null")
                        {
                            builder.AppendLine($"- 行业板块指数：{latestPrice}");
                        }

                        if (!string.IsNullOrEmpty(changeAmount) && changeAmount != "null")
                        {
                            builder.AppendLine($"- 涨跌额：{changeAmount}");
                        }

                        if (!string.IsNullOrEmpty(changePercent) && changePercent != "null")
                        {
                            builder.AppendLine($"- 涨跌幅：{changePercent}%");
                        }

                        if (!string.IsNullOrEmpty(totalMarketCap) && totalMarketCap != "null")
                        {
                            if (decimal.TryParse(totalMarketCap, out var marketCapDecimal))
                            {
                                var marketCapBillion = marketCapDecimal / 1_000_000_000M;
                                builder.AppendLine($"- 行业总市值：{marketCapBillion:F2}亿元");
                            }
                            else
                            {
                                builder.AppendLine($"- 行业总市值：{totalMarketCap}");
                            }
                        }

                        if (!string.IsNullOrEmpty(turnoverRate) && turnoverRate != "null")
                        {
                            builder.AppendLine($"- 换手率：{turnoverRate}%");
                        }

                        if (!string.IsNullOrEmpty(risingCount) && risingCount != "null" &&
                            !string.IsNullOrEmpty(fallingCount) && fallingCount != "null")
                        {
                            builder.AppendLine($"- 上涨家数：{risingCount}，下跌家数：{fallingCount}");
                        }

                        if (!string.IsNullOrEmpty(leaderStock))
                        {
                            AddKeyword(leaderStock);
                            var leaderInfo = $"- 领涨股票：{leaderStock}";
                            if (!string.IsNullOrEmpty(leaderChangePercent) && leaderChangePercent != "null")
                            {
                                leaderInfo += $"（涨跌幅：{leaderChangePercent}%）";
                            }
                            builder.AppendLine(leaderInfo);
                        }

                        builder.AppendLine();
                    }

                    if (industryPerformance != null)
                    {
                        var avgPE = industryPerformance["avgPE"]?.ToString() ?? "N/A";
                        var avgPB = industryPerformance["avgPB"]?.ToString() ?? "N/A";
                        var avgROE = industryPerformance["avgROE"]?.ToString() ?? "N/A";
                        var totalMarketCapPerformance = industryPerformance["totalMarketCap"]?.ToString() ?? "N/A";
                        var avgChangePercent = industryPerformance["avgChangePercent"]?.ToString() ?? "N/A";

                        builder.AppendLine("**行业表现指标：**");
                        builder.AppendLine($"- 行业平均市盈率(PE)：{avgPE}");
                        builder.AppendLine($"- 行业平均市净率(PB)：{avgPB}");
                        builder.AppendLine($"- 行业平均ROE：{avgROE}");
                        builder.AppendLine($"- 行业总市值：{totalMarketCapPerformance}");
                        builder.AppendLine($"- 行业平均涨跌幅：{avgChangePercent}%");
                        builder.AppendLine();
                    }

                    if (!string.IsNullOrEmpty(industryTrends))
                    {
                        builder.AppendLine("**行业趋势分析：**");
                        builder.AppendLine(industryTrends);
                        builder.AppendLine();
                    }

                    if (industryStocks != null && industryStocks.Count > 0)
                    {
                        builder.AppendLine($"**行业内主要股票（共{industryStocks.Count}只）：**");
                        int displayCount = Math.Min(industryStocks.Count, 20);
                        for (int i = 0; i < displayCount; i++)
                        {
                            var stock = industryStocks[i] as JObject;
                            if (stock != null)
                            {
                                var code = stock["code"]?.ToString() ?? string.Empty;
                                var name = stock["name"]?.ToString() ?? string.Empty;
                                var price = stock["price"]?.ToString() ?? "N/A";
                                var changePercent = stock["changePercent"]?.ToString() ?? "N/A";

                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    AddKeyword(name);
                                }

                                if (!string.IsNullOrWhiteSpace(code))
                                {
                                    AddKeyword(code);
                                }

                                builder.AppendLine($"- {name}({code}) 价格：{price}元 涨跌幅：{changePercent}%");
                            }
                        }

                        if (industryStocks.Count > displayCount)
                        {
                            builder.AppendLine($"... 还有{industryStocks.Count - displayCount}只股票未显示");
                        }

                        builder.AppendLine();
                    }

                    builder.AppendLine("**提示：请结合以上行业数据，分析该股票在所属行业中的地位、行业整体发展趋势，以及行业对该股票的影响。**");

                    var keywordList = keywords
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToList();

                    return new IndustryInfoResult
                    {
                        InfoText = builder.ToString(),
                        IndustryName = string.Equals(industryName, "未知", StringComparison.OrdinalIgnoreCase) ? null : industryName,
                        IndustryCode = string.IsNullOrWhiteSpace(industryCode) ? null : industryCode,
                        Keywords = keywordList
                    };
                }
            }

            return null;
        }
        catch (HttpRequestException ex)
        {
            if (ex.Message.Contains("404") || ex.Message.Contains("NOT FOUND"))
            {
                _logger.LogDebug(ex, "Python服务返回404 - 股票代码 {StockCode} 的行业数据未找到", stockCode);
            }
            else
            {
                _logger.LogDebug(ex, "Python服务不可用（可能未启动）");
            }
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("Timeout"))
        {
            _logger.LogWarning(ex, "Python服务请求超时");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Python服务调用失败");
            return null;
        }
    }
}
