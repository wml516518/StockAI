using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class MarketService : IMarketService
{
    private readonly ILogger<MarketService> _logger;
    private readonly HttpClient _httpClient;

    public MarketService(ILogger<MarketService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<string> GetHotRankFromAKShareAsync(string stockCode)
    {
        try
        {
            var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                ?? "http://localhost:5001";
            
            var normalizedStockCode = (stockCode ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalizedStockCode))
            {
                return string.Empty;
            }

            var encodedStockCode = Uri.EscapeDataString(normalizedStockCode);
            var url = $"{pythonServiceUrl}/api/stock/hot-rank/{encodedStockCode}";
            
            _logger.LogDebug("尝试从Python服务获取个股人气榜数据: {Url}", url);
            
            using var pythonClient = new HttpClient();
            pythonClient.Timeout = TimeSpan.FromSeconds(120);
            pythonClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            var response = await pythonClient.GetAsync(url);
            
            // 如果返回404，说明数据未找到，返回空字符串
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Python服务(AKShare)无法获取个股人气榜数据");
                return "";
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Python服务返回错误状态码: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return "";
            }
            
            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonData = JObject.Parse(responseContent);
            
            if (jsonData["success"]?.ToObject<bool>() == true)
            {
                var data = jsonData["data"] as JObject;
                if (data == null)
                {
                    _logger.LogInformation("未从Python服务获取到有效的人气榜数据");
                    return "";
                }

                static string FormatChange(string? label, int? value)
                {
                    if (!value.HasValue)
                    {
                        return $"{label}: 暂无数据";
                    }

                    var sign = value.Value > 0 ? "+" : string.Empty;
                    return $"{label}: {sign}{value}";
                }

                int? ParseNullableInt(JToken? token)
                {
                    if (token == null)
                    {
                        return null;
                    }

                    if (int.TryParse(token.ToString(), out var parsedInt))
                    {
                        return parsedInt;
                    }

                    if (double.TryParse(token.ToString(), out var parsedDouble))
                    {
                        return (int)Math.Round(parsedDouble);
                    }

                    return null;
                }

                var rank = ParseNullableInt(data["rank"]);
                var rankChange = ParseNullableInt(data["rankChange"]);
                var hisRankChange = ParseNullableInt(data["hisRankChange"]);
                var marketAllCount = ParseNullableInt(data["marketAllCount"]);
                var calcTime = data["calcTime"]?.ToString();
                var symbol = data["symbol"]?.ToString() ?? normalizedStockCode;
                var innerCode = data["innerCode"]?.ToString();

                var builder = new StringBuilder();
                builder.AppendLine();
                builder.AppendLine("【个股人气榜数据】（数据来源：AKShare - stock_hot_rank_latest_em）");
                if (!string.IsNullOrWhiteSpace(calcTime))
                {
                    builder.AppendLine($"更新时间：{calcTime}");
                }

                builder.AppendLine();

                if (rank.HasValue)
                {
                    var totalText = marketAllCount.HasValue ? $"/ 共{marketAllCount}只股票" : string.Empty;
                    builder.AppendLine($"**股票 {symbol} 当前人气排名: 第{rank}{totalText}**");
                    builder.AppendLine();
                    builder.AppendLine("**排名变化信息：**");
                    builder.AppendLine($"- {FormatChange("与上一期相比的排名变化", rankChange)}");
                    builder.AppendLine($"- {FormatChange("历史区间排名变化", hisRankChange)}");
                }
                else
                {
                    builder.AppendLine("当前未能获取到该股票的人气排名数据。");
                }

                if (!string.IsNullOrWhiteSpace(innerCode))
                {
                    builder.AppendLine();
                    builder.AppendLine($"内部代码：{innerCode}");
                }

                builder.AppendLine();
                builder.AppendLine("**提示：请结合人气排名及其变化，分析市场关注度与情绪趋势，对投资决策进行辅助判断。**");

                return builder.ToString();
            }

            return "";
        }
        catch (HttpRequestException ex)
        {
            if (ex.Message.Contains("404") || ex.Message.Contains("NOT FOUND"))
            {
                _logger.LogDebug(ex, "Python服务返回404 - 个股人气榜数据未找到");
            }
            else
            {
                _logger.LogDebug(ex, "Python服务不可用（可能未启动）");
            }
            return "";
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("Timeout"))
        {
            _logger.LogWarning(ex, "Python服务请求超时");
            return "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Python服务调用失败");
            return "";
        }
    }
}
