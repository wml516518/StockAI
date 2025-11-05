using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Services.Interfaces;
using StockAnalyse.Api.Models;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<AIController> _logger;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public AIController(IAIService aiService, IStockDataService stockDataService, ILogger<AIController> logger, IHttpClientFactory httpClientFactory, IMemoryCache cache)
    {
        _aiService = aiService;
        _stockDataService = stockDataService;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _cache = cache;
    }

    /// <summary>
    /// 分析股票（可指定提示词）
    /// </summary>
    [HttpPost("analyze/{stockCode}")]
    public async Task<ActionResult<string>> AnalyzeStock(string stockCode, [FromBody] AnalyzeRequest request)
    {
        Console.WriteLine("============================================");
        Console.WriteLine($"[AI分析] 开始分析股票: {stockCode}");
        Console.WriteLine($"============================================");
        
        _logger.LogInformation("============================================");
        _logger.LogInformation("🤖 [AIController] 开始分析股票: {StockCode}", stockCode);
        _logger.LogInformation("============================================");
        
        try
        {
            // 获取股票基本面和实时行情数据
            // 注意：GetFundamentalInfoAsync会自动优先使用Python服务（AKShare），如果不可用则回退到其他数据源
            Console.WriteLine($"[AI分析] 步骤1: 正在获取股票 {stockCode} 的基本面信息（优先使用Python服务/AKShare数据源）...");
            _logger.LogInformation("🤖 [AIController] 步骤1: 正在获取股票基本面信息（优先使用Python服务/AKShare数据源）...");
            
            StockFundamentalInfo? fundamentalInfo = null;
            string? dataSource = null;
            try
            {
                fundamentalInfo = await _stockDataService.GetFundamentalInfoAsync(stockCode);
                
                // 根据获取到的数据判断数据源
                // 如果Python服务成功，通常会有更完整的财务数据
                if (fundamentalInfo != null)
                {
                    // 检查是否有完整的财务数据（Python服务通常提供更多字段）
                    if (fundamentalInfo.TotalRevenue.HasValue && fundamentalInfo.NetProfit.HasValue && 
                        fundamentalInfo.ROE.HasValue && fundamentalInfo.EPS.HasValue)
                    {
                        dataSource = "Python服务 (AKShare)";
                    }
                    else if (fundamentalInfo.PE.HasValue || fundamentalInfo.PB.HasValue)
                    {
                        dataSource = "实时行情接口";
                    }
                    else
                    {
                        dataSource = "备用数据源";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI分析] ❌ 获取基本面信息时发生异常: {ex.Message}");
                Console.WriteLine($"[AI分析] 异常类型: {ex.GetType().Name}");
                _logger.LogError(ex, "🤖 [AIController] ❌ 获取基本面信息时发生异常");
                // 继续执行，使用null值
            }
            
            if (fundamentalInfo != null)
            {
                Console.WriteLine($"[AI分析] ✅ 成功获取基本面信息！数据来源: {dataSource ?? "未知"}");
                Console.WriteLine($"[AI分析]   股票名称: {fundamentalInfo.StockName}");
                Console.WriteLine($"[AI分析]   报告期: {fundamentalInfo.ReportDate ?? "未知"}");
                if (!string.IsNullOrEmpty(fundamentalInfo.ReportType))
                {
                    Console.WriteLine($"[AI分析]   报告类型: {fundamentalInfo.ReportType}");
                }
                Console.WriteLine($"[AI分析]   营业收入: {(fundamentalInfo.TotalRevenue.HasValue ? fundamentalInfo.TotalRevenue.Value.ToString("F2") + "万元" : "N/A")}");
                Console.WriteLine($"[AI分析]   净利润: {(fundamentalInfo.NetProfit.HasValue ? fundamentalInfo.NetProfit.Value.ToString("F2") + "万元" : "N/A")}");
                Console.WriteLine($"[AI分析]   ROE: {(fundamentalInfo.ROE.HasValue ? fundamentalInfo.ROE.Value.ToString("F2") + "%" : "N/A")}");
                Console.WriteLine($"[AI分析]   营收增长率: {(fundamentalInfo.RevenueGrowthRate.HasValue ? fundamentalInfo.RevenueGrowthRate.Value.ToString("F2") + "%" : "N/A")}");
                Console.WriteLine($"[AI分析]   EPS: {(fundamentalInfo.EPS.HasValue ? fundamentalInfo.EPS.Value.ToString("F3") + "元" : "N/A")}");
                Console.WriteLine($"[AI分析]   PE: {(fundamentalInfo.PE?.ToString("F2") ?? "N/A")}");
                Console.WriteLine($"[AI分析]   PB: {(fundamentalInfo.PB?.ToString("F2") ?? "N/A")}");
                
                _logger.LogInformation("🤖 [AIController] ✅ 成功获取基本面信息 - 数据来源: {DataSource}, 股票: {StockName}, 报告期: {ReportDate}", 
                    dataSource ?? "未知", fundamentalInfo.StockName, fundamentalInfo.ReportDate);
            }
            else
            {
                Console.WriteLine($"[AI分析] ⚠️ 未能获取基本面信息，将使用实时行情数据");
                Console.WriteLine($"[AI分析] 💡 提示: 如果Python服务未启动，请运行 start-all-services.ps1 启动所有服务");
                _logger.LogWarning("🤖 [AIController] ⚠️ 未能获取基本面信息，将使用实时行情数据");
            }
            
            Console.WriteLine($"[AI分析] 步骤2: 正在获取股票 {stockCode} 的实时行情...");
            _logger.LogInformation("🤖 [AIController] 步骤2: 正在获取实时行情...");
            
            var stock = await _stockDataService.GetRealTimeQuoteAsync(stockCode);
            
            if (stock != null)
            {
                Console.WriteLine($"[AI分析] ✅ 成功获取实时行情！");
                Console.WriteLine($"[AI分析]   股票名称: {stock.Name}");
                Console.WriteLine($"[AI分析]   当前价格: {stock.CurrentPrice:F2}元");
                Console.WriteLine($"[AI分析]   涨跌幅: {stock.ChangePercent:F2}%");
                Console.WriteLine($"[AI分析]   PE: {(stock.PE?.ToString("F2") ?? "N/A")}");
                Console.WriteLine($"[AI分析]   PB: {(stock.PB?.ToString("F2") ?? "N/A")}");
                
                _logger.LogInformation("🤖 [AIController] ✅ 成功获取实时行情 - 股票: {StockName}, 价格: {Price}, 涨跌幅: {ChangePercent}%", 
                    stock.Name, stock.CurrentPrice, stock.ChangePercent);
            }
            else
            {
                Console.WriteLine($"[AI分析] ⚠️ 未能获取实时行情");
                _logger.LogWarning("🤖 [AIController] ⚠️ 未能获取实时行情");
            }
            
            // 步骤2.4: 获取近3个月的历史交易数据
            Console.WriteLine($"[AI分析] 步骤2.4: 正在获取股票 {stockCode} 近3个月的历史交易数据...");
            _logger.LogInformation("🤖 [AIController] 步骤2.4: 正在获取近3个月历史交易数据...");
            
            var endDate = DateTime.Now;
            var startDate = endDate.AddMonths(-3);
            List<StockHistory> historyData = new List<StockHistory>();
            
            try
            {
                // 计算理论交易日数量（排除周末，但保留节假日，因为节假日也可能有数据）
                // 近3个月约90天，去掉周末约26天，理论交易日约64天
                int totalDays = (int)(endDate - startDate).TotalDays;
                int weekendDays = 0;
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                        weekendDays++;
                }
                int theoreticalTradingDays = totalDays - weekendDays; // 理论上限（不考虑节假日）
                
                Console.WriteLine($"[AI分析] 📊 数据统计：");
                Console.WriteLine($"[AI分析]   查询时间范围：{startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}");
                Console.WriteLine($"[AI分析]   总天数：{totalDays}天");
                Console.WriteLine($"[AI分析]   周末天数：{weekendDays}天");
                Console.WriteLine($"[AI分析]   理论交易日（上限）：约{theoreticalTradingDays}天（实际交易日会因节假日减少）");
                
                // 先从数据库获取历史数据
                historyData = await _stockDataService.GetDailyDataAsync(stockCode, startDate, endDate);
                
                Console.WriteLine($"[AI分析]   数据库中的历史数据：{historyData.Count}条");
                
                // 如果数据不足（少于理论交易日的70%），则从API拉取
                int minExpectedDays = (int)(theoreticalTradingDays * 0.7); // 至少应该有理论交易日的70%
                if (historyData.Count < minExpectedDays)
                {
                    Console.WriteLine($"[AI分析] ⚠️ 数据库中的历史数据不足（仅{historyData.Count}条，期望至少{minExpectedDays}条），正在从API拉取...");
                    _logger.LogInformation("🤖 [AIController] 数据库历史数据不足（{Count}条，期望{Expected}条），从API拉取", historyData.Count, minExpectedDays);
                    
                    int fetchedCount = await _stockDataService.FetchAndStoreDailyHistoryAsync(stockCode, startDate, endDate);
                    Console.WriteLine($"[AI分析] ✅ 从API拉取了 {fetchedCount} 条历史数据");
                    _logger.LogInformation("🤖 [AIController] 从API拉取了 {Count} 条历史数据", fetchedCount);
                    
                    // 重新从数据库获取
                    historyData = await _stockDataService.GetDailyDataAsync(stockCode, startDate, endDate);
                }
                
                if (historyData.Count > 0)
                {
                    // 验证数据完整性
                    var sortedHistory = historyData.OrderBy(h => h.TradeDate).ToList();
                    var firstDate = sortedHistory.First().TradeDate;
                    var lastDate = sortedHistory.Last().TradeDate;
                    var actualDateRange = (lastDate - firstDate).TotalDays;
                    
                    // 检查数据连续性（检测是否有明显的日期缺失）
                    var dateSet = sortedHistory.Select(h => h.TradeDate.Date).ToHashSet();
                    int gaps = 0;
                    var missingDates = new List<DateTime>();
                    for (var date = firstDate.Date; date <= lastDate.Date; date = date.AddDays(1))
                    {
                        // 只检查工作日（周一到周五）
                        if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                        {
                            if (!dateSet.Contains(date))
                            {
                                gaps++;
                                missingDates.Add(date);
                            }
                        }
                    }
                    
                    Console.WriteLine($"[AI分析] ✅ 成功获取 {historyData.Count} 条历史交易数据");
                    Console.WriteLine($"[AI分析] 📊 数据验证信息：");
                    Console.WriteLine($"[AI分析]   最早交易日：{firstDate:yyyy-MM-dd}");
                    Console.WriteLine($"[AI分析]   最新交易日：{lastDate:yyyy-MM-dd}");
                    Console.WriteLine($"[AI分析]   实际时间跨度：{actualDateRange:F0}天");
                    Console.WriteLine($"[AI分析]   数据完整性：{historyData.Count}/{theoreticalTradingDays} ({historyData.Count * 100.0 / theoreticalTradingDays:F1}%)");
                    
                    if (gaps > 0)
                    {
                        Console.WriteLine($"[AI分析]   ⚠️ 检测到 {gaps} 个工作日可能缺失数据（可能是节假日、停牌等）");
                        if (missingDates.Count <= 10)
                        {
                            Console.WriteLine($"[AI分析]   缺失日期：{string.Join(", ", missingDates.Select(d => d.ToString("yyyy-MM-dd")))}");
                        }
                        else
                        {
                            Console.WriteLine($"[AI分析]   缺失日期（前10个）：{string.Join(", ", missingDates.Take(10).Select(d => d.ToString("yyyy-MM-dd")))}...");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[AI分析]   ✅ 数据连续性良好，无明显的日期缺失");
                    }
                    
                    // 数据可靠性评估
                    double completenessRatio = historyData.Count * 100.0 / theoreticalTradingDays;
                    if (completenessRatio >= 85)
                    {
                        Console.WriteLine($"[AI分析]   ✅ 数据可靠性：优秀（完整度{completenessRatio:F1}%）");
                    }
                    else if (completenessRatio >= 70)
                    {
                        Console.WriteLine($"[AI分析]   ⚠️ 数据可靠性：良好（完整度{completenessRatio:F1}%，可能缺少部分交易日数据）");
                    }
                    else
                    {
                        Console.WriteLine($"[AI分析]   ❌ 数据可靠性：不足（完整度{completenessRatio:F1}%，建议检查数据源）");
                    }
                    
                    _logger.LogInformation("🤖 [AIController] ✅ 成功获取 {Count} 条历史交易数据（时间范围：{FirstDate} 至 {LastDate}，完整度：{Completeness:F1}%）", 
                        historyData.Count, firstDate, lastDate, completenessRatio);
                }
                else
                {
                    Console.WriteLine($"[AI分析] ⚠️ 未能获取历史交易数据");
                    _logger.LogWarning("🤖 [AIController] ⚠️ 未能获取历史交易数据");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI分析] ❌ 获取历史交易数据时发生异常: {ex.Message}");
                _logger.LogError(ex, "🤖 [AIController] ❌ 获取历史交易数据时发生异常");
                // 继续执行，使用空列表
            }
            
            // 步骤2.5: 获取交易数据（分时成交、买卖盘口）并缓存
            string tradeDataText = "";
            try
            {
                Console.WriteLine($"[AI分析] 步骤2.5: 获取交易数据（分时成交、买卖盘口）...");
                _logger.LogInformation("🤖 [AIController] 步骤2.5: 获取交易数据");
                
                // 检查缓存（缓存5分钟）
                var cacheKey = $"trade_data_{stockCode}";
                if (!_cache.TryGetValue(cacheKey, out string? cachedTradeData))
                {
                    var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                        ?? "http://localhost:5001";
                    
                    var tradeUrl = $"{pythonServiceUrl}/api/stock/trade/{stockCode}?data_type=all";
                    
                    using var tradeClient = new HttpClient();
                    tradeClient.Timeout = TimeSpan.FromSeconds(30);
                    tradeClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    
                    var tradeResponse = await tradeClient.GetAsync(tradeUrl);
                    
                    if (tradeResponse.IsSuccessStatusCode)
                    {
                        var tradeContent = await tradeResponse.Content.ReadAsStringAsync();
                        var tradeJson = Newtonsoft.Json.Linq.JObject.Parse(tradeContent);
                        
                        if (tradeJson["success"]?.ToString() == "True" && tradeJson["data"] != null)
                        {
                            var tradeData = tradeJson["data"] as Newtonsoft.Json.Linq.JObject;
                            
                            if (tradeData != null)
                            {
                                var dataSection = tradeData["data"] as Newtonsoft.Json.Linq.JObject;
                                
                                if (dataSection != null)
                                {
                                    // 格式化分时成交数据
                                    var minuteData = dataSection["minute"] as Newtonsoft.Json.Linq.JObject;
                                    var bidAskData = dataSection["bidAsk"] as Newtonsoft.Json.Linq.JObject;
                                    
                                    tradeDataText = "\n\n【实时交易数据】\n";
                                    
                                    // 分时成交数据
                                    if (minuteData != null && minuteData["success"]?.ToString() == "True")
                                    {
                                        var records = minuteData["records"] as Newtonsoft.Json.Linq.JArray;
                                        var count = minuteData["count"]?.ToString() ?? "0";
                                        
                                        if (records != null && records.Count > 0)
                                        {
                                            tradeDataText += $"\n**分时成交数据**（共{count}条，显示最近{Math.Min(records.Count, 20)}条）：\n";
                                            
                                            // 只显示最近20条
                                            var recentRecords = records.TakeLast(20).ToList();
                                            
                                            foreach (var record in recentRecords)
                                            {
                                                var rec = record as Newtonsoft.Json.Linq.JObject;
                                                if (rec != null)
                                                {
                                                    var time = rec["time"]?.ToString() ?? "";
                                                    var open = rec["open"]?.ToString() ?? "0";
                                                    var high = rec["high"]?.ToString() ?? "0";
                                                    var low = rec["low"]?.ToString() ?? "0";
                                                    var close = rec["close"]?.ToString() ?? "0";
                                                    var volume = rec["volume"]?.ToString() ?? "0";
                                                    
                                                    tradeDataText += $"- {time}: 开{open} 高{high} 低{low} 收{close} 量{volume}\n";
                                                }
                                            }
                                            
                                            // 计算分时数据统计
                                            var prices = recentRecords.Select(r => 
                                                decimal.TryParse((r as Newtonsoft.Json.Linq.JObject)?["close"]?.ToString(), out var p) ? p : 0
                                            ).Where(p => p > 0).ToList();
                                            
                                            if (prices.Count > 0)
                                            {
                                                var maxPrice = prices.Max();
                                                var minPrice = prices.Min();
                                                var avgPrice = prices.Average();
                                                var firstPrice = prices.First();
                                                var lastPrice = prices.Last();
                                                
                                                tradeDataText += $"\n*分时数据统计（最近{recentRecords.Count}条）：*\n";
                                                tradeDataText += $"- 最高价：{maxPrice:F2}元\n";
                                                tradeDataText += $"- 最低价：{minPrice:F2}元\n";
                                                tradeDataText += $"- 平均价：{avgPrice:F2}元\n";
                                                tradeDataText += $"- 价格变化：{lastPrice - firstPrice:+#.##;-#.##;0}元（{((lastPrice - firstPrice) / firstPrice * 100):+#.##;-#.##;0}%）\n";
                                            }
                                        }
                                    }
                                    
                                    // 买卖盘口数据
                                    if (bidAskData != null && bidAskData["success"]?.ToString() == "True")
                                    {
                                        var bidAskDataSection = bidAskData["data"] as Newtonsoft.Json.Linq.JObject;
                                        
                                        if (bidAskDataSection != null)
                                        {
                                            tradeDataText += $"\n**买卖盘口数据**：\n";
                                            
                                            // 解析买卖盘数据
                                            var sellData = new Dictionary<int, (decimal price, decimal volume)>();
                                            var buyData = new Dictionary<int, (decimal price, decimal volume)>();
                                            
                                            foreach (var prop in bidAskDataSection.Properties())
                                            {
                                                var key = prop.Name;
                                                var value = decimal.TryParse(prop.Value?.ToString(), out var v) ? v : 0;
                                                
                                                if (key.StartsWith("sell_") && key.EndsWith("_vol"))
                                                {
                                                    var level = int.TryParse(key.Replace("sell_", "").Replace("_vol", ""), out var l) ? l : 0;
                                                    if (level > 0 && sellData.ContainsKey(level))
                                                    {
                                                        sellData[level] = (sellData[level].price, value);
                                                    }
                                                }
                                                else if (key.StartsWith("sell_") && !key.EndsWith("_vol"))
                                                {
                                                    var level = int.TryParse(key.Replace("sell_", ""), out var l) ? l : 0;
                                                    if (level > 0)
                                                    {
                                                        if (sellData.ContainsKey(level))
                                                        {
                                                            sellData[level] = (value, sellData[level].volume);
                                                        }
                                                        else
                                                        {
                                                            sellData[level] = (value, 0);
                                                        }
                                                    }
                                                }
                                                else if (key.StartsWith("buy_") && key.EndsWith("_vol"))
                                                {
                                                    var level = int.TryParse(key.Replace("buy_", "").Replace("_vol", ""), out var l) ? l : 0;
                                                    if (level > 0 && buyData.ContainsKey(level))
                                                    {
                                                        buyData[level] = (buyData[level].price, value);
                                                    }
                                                }
                                                else if (key.StartsWith("buy_") && !key.EndsWith("_vol"))
                                                {
                                                    var level = int.TryParse(key.Replace("buy_", ""), out var l) ? l : 0;
                                                    if (level > 0)
                                                    {
                                                        if (buyData.ContainsKey(level))
                                                        {
                                                            buyData[level] = (value, buyData[level].volume);
                                                        }
                                                        else
                                                        {
                                                            buyData[level] = (value, 0);
                                                        }
                                                    }
                                                }
                                            }
                                            
                                            // 显示卖盘（从卖5到卖1）
                                            if (sellData.Count > 0)
                                            {
                                                tradeDataText += "\n*卖盘（从高到低）：*\n";
                                                foreach (var kvp in sellData.OrderByDescending(x => x.Key))
                                                {
                                                    tradeDataText += $"- 卖{kvp.Key}：{kvp.Value.price:F2}元 量{kvp.Value.volume:F0}手\n";
                                                }
                                            }
                                            
                                            // 显示买盘（从买1到买5）
                                            if (buyData.Count > 0)
                                            {
                                                tradeDataText += "\n*买盘（从高到低）：*\n";
                                                foreach (var kvp in buyData.OrderByDescending(x => x.Key))
                                                {
                                                    tradeDataText += $"- 买{kvp.Key}：{kvp.Value.price:F2}元 量{kvp.Value.volume:F0}手\n";
                                                }
                                            }
                                            
                                            // 分析买卖盘口
                                            if (sellData.Count > 0 && buyData.Count > 0)
                                            {
                                                var sell1Price = sellData.ContainsKey(1) ? sellData[1].price : 0;
                                                var buy1Price = buyData.ContainsKey(1) ? buyData[1].price : 0;
                                                
                                                if (sell1Price > 0 && buy1Price > 0)
                                                {
                                                    var spread = sell1Price - buy1Price;
                                                    var spreadPercent = (spread / buy1Price) * 100;
                                                    
                                                    tradeDataText += $"\n*盘口分析：*\n";
                                                    tradeDataText += $"- 卖一价：{sell1Price:F2}元\n";
                                                    tradeDataText += $"- 买一价：{buy1Price:F2}元\n";
                                                    tradeDataText += $"- 价差：{spread:F2}元（{spreadPercent:F2}%）\n";
                                                    
                                                    var totalSellVolume = sellData.Values.Sum(v => v.volume);
                                                    var totalBuyVolume = buyData.Values.Sum(v => v.volume);
                                                    
                                                    tradeDataText += $"- 卖盘总量：{totalSellVolume:F0}手\n";
                                                    tradeDataText += $"- 买盘总量：{totalBuyVolume:F0}手\n";
                                                    tradeDataText += $"- 买卖比：{(totalBuyVolume > 0 ? (totalSellVolume / totalBuyVolume).ToString("F2") : "N/A")}\n";
                                                }
                                            }
                                        }
                                    }
                                    
                                    tradeDataText += "\n**提示：请结合以上实时交易数据（分时成交、买卖盘口），分析当前市场情绪和交易活跃度，判断买卖力量的对比。**\n";
                                    
                                    // 缓存5分钟
                                    _cache.Set(cacheKey, tradeDataText, TimeSpan.FromMinutes(5));
                                    
                                    Console.WriteLine($"[AI分析] ✅ 交易数据获取完成！数据长度: {tradeDataText.Length} 字符");
                                    _logger.LogInformation("🤖 [AIController] ✅ 交易数据获取完成，已缓存");
                                }
                            }
                        }
                    }
                }
                else
                {
                    tradeDataText = cachedTradeData ?? "";
                    Console.WriteLine($"[AI分析] ✅ 使用缓存的交易数据");
                    _logger.LogInformation("🤖 [AIController] ✅ 使用缓存的交易数据");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI分析] ⚠️ 获取交易数据时发生异常: {ex.Message}");
                _logger.LogWarning(ex, "🤖 [AIController] ⚠️ 获取交易数据时发生异常");
                // 继续执行，不影响其他分析
            }
            
            // 步骤2.6: 调用Python服务进行大数据分析（AKShare数据源）
            string pythonAnalysisText = "";
            try
            {
                Console.WriteLine($"[AI分析] 步骤2.6: 调用Python服务进行大数据分析（AKShare数据源）...");
                _logger.LogInformation("🤖 [AIController] 步骤2.6: 调用Python服务进行大数据分析");
                
                var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                    ?? "http://localhost:5001";
                
                var analyzeUrl = $"{pythonServiceUrl}/api/stock/analyze/{stockCode}?months=3";
                
                // 创建独立的HttpClient，设置更长的超时时间（Python分析需要获取数据并计算指标，可能需要较长时间）
                using var pythonClient = new HttpClient();
                pythonClient.Timeout = TimeSpan.FromSeconds(180); // 增加到180秒（3分钟），因为需要获取历史数据+分析
                pythonClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                
                Console.WriteLine($"[AI分析] 正在调用Python分析服务（超时时间：180秒）...");
                var analyzeResponse = await pythonClient.GetAsync(analyzeUrl);
                
                if (analyzeResponse.IsSuccessStatusCode)
                {
                    var analyzeContent = await analyzeResponse.Content.ReadAsStringAsync();
                    var analyzeJson = Newtonsoft.Json.Linq.JObject.Parse(analyzeContent);
                    
                    if (analyzeJson["success"]?.ToString() == "True" && analyzeJson["data"] != null)
                    {
                        var analysisData = analyzeJson["data"] as Newtonsoft.Json.Linq.JObject;
                        
                        if (analysisData != null)
                        {
                            // 格式化Python分析结果
                            var indicators = analysisData["indicators"] as Newtonsoft.Json.Linq.JObject;
                            var trends = analysisData["trends"] as Newtonsoft.Json.Linq.JObject;
                            var statistics = analysisData["statistics"] as Newtonsoft.Json.Linq.JObject;
                            var insights = analysisData["insights"] as Newtonsoft.Json.Linq.JArray;
                            
                            // 辅助函数：安全获取数值
                            Func<Newtonsoft.Json.Linq.JToken?, string> SafeGetDouble = (token) => 
                                token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.Float || token.Type == Newtonsoft.Json.Linq.JTokenType.Integer 
                                    ? ((double)token).ToString("F2") : "N/A";
                            
                            Func<Newtonsoft.Json.Linq.JToken?, string> SafeGetDouble4 = (token) => 
                                token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.Float || token.Type == Newtonsoft.Json.Linq.JTokenType.Integer 
                                    ? ((double)token).ToString("F4") : "N/A";
                            
                            Func<Newtonsoft.Json.Linq.JToken?, string> SafeGetString = (token) => 
                                token != null ? token.ToString() : "N/A";
                            
                            var ma = indicators?["MA"] as Newtonsoft.Json.Linq.JObject;
                            var macd = indicators?["MACD"] as Newtonsoft.Json.Linq.JObject;
                            var rsi = indicators?["RSI"] as Newtonsoft.Json.Linq.JObject;
                            var bb = indicators?["BollingerBands"] as Newtonsoft.Json.Linq.JObject;
                            
                            pythonAnalysisText = $@"

【Python大数据分析结果】（基于AKShare数据源，分析期：{SafeGetString(analysisData["period"])}，数据条数：{SafeGetString(analysisData["totalRecords"])}条）

**基础统计信息：**
- 期初价格：{SafeGetDouble(statistics?["startPrice"])}元
- 期末价格：{SafeGetDouble(statistics?["endPrice"])}元
- 最高价：{SafeGetDouble(statistics?["highestPrice"])}元
- 最低价：{SafeGetDouble(statistics?["lowestPrice"])}元
- 平均价格：{SafeGetDouble(statistics?["averagePrice"])}元
- 价格涨跌幅：{SafeGetDouble(statistics?["priceChange"])}元（{SafeGetDouble(statistics?["priceChangePercent"])}%）
- 波动率：{SafeGetDouble(statistics?["volatility"])}%

**技术指标分析：**

*移动平均线(MA)：*
- MA5：{SafeGetDouble(ma?["MA5"])}元
- MA10：{SafeGetDouble(ma?["MA10"])}元
- MA20：{SafeGetDouble(ma?["MA20"])}元
- MA60：{SafeGetDouble(ma?["MA60"])}元
- 趋势：{(SafeGetString(ma?["trend"]) == "up" ? "上升趋势" : "下降趋势")}

*MACD指标：*
- MACD值：{SafeGetDouble4(macd?["MACD"])}
- Signal信号线：{SafeGetDouble4(macd?["Signal"])}
- Histogram柱状图：{SafeGetDouble4(macd?["Histogram"])}
- 信号：{(SafeGetString(macd?["signal"]) == "bullish" ? "看涨信号" : "看跌信号")}

*RSI相对强弱指标：*
- RSI值：{SafeGetDouble(rsi?["RSI"])}
- 信号：{(SafeGetString(rsi?["signal"]) == "overbought" ? "超买（>70）" : SafeGetString(rsi?["signal"]) == "oversold" ? "超卖（<30）" : "中性（30-70）")}

*布林带(Bollinger Bands)：*
- 上轨：{SafeGetDouble(bb?["Upper"])}元
- 中轨：{SafeGetDouble(bb?["Middle"])}元
- 下轨：{SafeGetDouble(bb?["Lower"])}元
- 价格位置：{(SafeGetString(bb?["position"]) == "above" ? "上轨上方（超买）" : SafeGetString(bb?["position"]) == "below" ? "下轨下方（超卖）" : "中轨附近（正常）")}

**趋势分析：**
- 价格趋势：{(SafeGetString(trends?["priceTrend"]) == "up" ? "上升" : "下降")}
- 成交量趋势：{(SafeGetString(trends?["volumeTrend"]) == "increase" ? "放大" : "萎缩")}
- 动量：{(SafeGetString(trends?["momentum"]) == "strong" ? "强劲" : "温和")}
- 波动率趋势：{(SafeGetString(trends?["volatilityTrend"]) == "high" ? "高波动" : "低波动")}

**关键洞察：**
";
                            
                            if (insights != null && insights.Count > 0)
                            {
                                foreach (var insight in insights)
                                {
                                    pythonAnalysisText += $"- {insight}\n";
                                }
                            }
                            
                            pythonAnalysisText += $@"

**提示：请结合以上Python大数据分析结果（技术指标、趋势分析等），结合基本面信息和历史交易数据，给出综合的投资建议和未来走势预测。**
";
                            
                            Console.WriteLine($"[AI分析] ✅ Python大数据分析完成！分析结果长度: {pythonAnalysisText.Length} 字符");
                            _logger.LogInformation("🤖 [AIController] ✅ Python大数据分析完成，结果长度: {Length} 字符", pythonAnalysisText.Length);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[AI分析] ⚠️ Python分析服务返回失败: {analyzeJson["error"]?.ToString() ?? "未知错误"}");
                        _logger.LogWarning("🤖 [AIController] ⚠️ Python分析服务返回失败");
                    }
                }
                else
                {
                    Console.WriteLine($"[AI分析] ⚠️ Python分析服务不可用（状态码: {(int)analyzeResponse.StatusCode}），将使用基础分析");
                    _logger.LogWarning("🤖 [AIController] ⚠️ Python分析服务不可用（状态码: {StatusCode}）", (int)analyzeResponse.StatusCode);
                }
            }
            catch (System.Threading.Tasks.TaskCanceledException ex) when (ex.InnerException is System.TimeoutException || ex.Message.Contains("Timeout"))
            {
                Console.WriteLine($"[AI分析] ⚠️ Python分析服务请求超时（已设置180秒超时）");
                Console.WriteLine($"[AI分析] 💡 提示: Python服务需要获取历史数据并计算技术指标，可能需要较长时间");
                Console.WriteLine($"[AI分析] 💡 系统将继续使用基础历史数据分析，不会影响AI分析结果");
                _logger.LogWarning(ex, "🤖 [AIController] ⚠️ Python分析服务请求超时");
                // 继续执行，使用基础分析
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI分析] ⚠️ 调用Python分析服务时发生异常: {ex.Message}");
                Console.WriteLine($"[AI分析] 💡 系统将继续使用基础历史数据分析");
                _logger.LogWarning(ex, "🤖 [AIController] ⚠️ 调用Python分析服务时发生异常");
                // 继续执行，使用基础分析
            }
            
            // 构建包含基本面信息的上下文
            string? enhancedContext = request?.Context;
            
            // 格式化历史交易数据
            string historyText = "";
            if (historyData != null && historyData.Count > 0)
            {
                Console.WriteLine($"[AI分析] 步骤3: 格式化历史交易数据...");
                _logger.LogInformation("🤖 [AIController] 步骤3: 格式化历史交易数据");
                
                // 按日期排序（从旧到新）
                var sortedHistory = historyData.OrderBy(h => h.TradeDate).ToList();
                
                if (sortedHistory.Count > 0)
                {
                    // 计算关键统计指标
                    var closes = sortedHistory.Select(h => h.Close).ToList();
                    var volumes = sortedHistory.Select(h => h.Volume).ToList();
                    var turnovers = sortedHistory.Select(h => h.Turnover).ToList();
                    
                    decimal maxPrice = closes.Max();
                    decimal minPrice = closes.Min();
                    decimal avgPrice = closes.Average();
                    decimal currentClose = closes.Last();
                    decimal firstClose = closes.First();
                    decimal priceChange = currentClose - firstClose;
                    decimal priceChangePercent = firstClose != 0 ? (priceChange / firstClose) * 100 : 0;
                    
                    decimal avgVolume = volumes.Average();
                    decimal maxVolume = volumes.Max();
                    decimal minVolume = volumes.Min();
                    decimal avgTurnover = turnovers.Average();
                    
                    // 计算价格趋势（最近10个交易日 vs 前10个交易日）
                    int recentDays = Math.Min(10, sortedHistory.Count);
                    int earlyDays = Math.Min(10, sortedHistory.Count);
                    decimal recentAvgPrice = sortedHistory.TakeLast(recentDays).Average(h => h.Close);
                    decimal earlyAvgPrice = sortedHistory.Take(earlyDays).Average(h => h.Close);
                    decimal trendPercent = earlyAvgPrice != 0 ? ((recentAvgPrice - earlyAvgPrice) / earlyAvgPrice) * 100 : 0;
                    
                    // 计算成交量趋势
                    decimal recentAvgVolume = sortedHistory.TakeLast(recentDays).Average(h => h.Volume);
                    decimal earlyAvgVolume = sortedHistory.Take(earlyDays).Average(h => h.Volume);
                    decimal volumeTrendPercent = earlyAvgVolume != 0 ? ((recentAvgVolume - earlyAvgVolume) / earlyAvgVolume) * 100 : 0;
                
                // 构建历史数据文本
                historyText = $@"

【近3个月交易数据统计】（共{historyData.Count}个交易日，从{startDate:yyyy-MM-dd}至{endDate:yyyy-MM-dd}）

**价格走势：**
- 期初价格：{firstClose:F2}元
- 期末价格：{currentClose:F2}元
- 期间涨跌：{priceChange:+#.##;-#.##;0}元（{priceChangePercent:+#.##;-#.##;0}%）
- 最高价：{maxPrice:F2}元
- 最低价：{minPrice:F2}元
- 平均价格：{avgPrice:F2}元

**价格趋势分析：**
- 最近{recentDays}个交易日平均价：{recentAvgPrice:F2}元
- 前{earlyDays}个交易日平均价：{earlyAvgPrice:F2}元
- 价格趋势：{(trendPercent > 0 ? "上涨" : trendPercent < 0 ? "下跌" : "持平")} {Math.Abs(trendPercent):F2}%

**成交量分析：**
- 平均成交量：{avgVolume:F0}手
- 最大成交量：{maxVolume:F0}手
- 最小成交量：{minVolume:F0}手
- 平均成交额：{avgTurnover:F2}万元
- 成交量趋势：{(volumeTrendPercent > 0 ? "放大" : volumeTrendPercent < 0 ? "萎缩" : "持平")} {Math.Abs(volumeTrendPercent):F2}%

**近期关键交易日数据（最近10个交易日）：**
";
                
                // 添加最近10个交易日的详细数据
                var recentHistory = sortedHistory.TakeLast(10).ToList();
                for (int i = 0; i < recentHistory.Count; i++)
                {
                    var day = recentHistory[i];
                    // 找到该日在完整列表中的索引
                    int dayIndex = sortedHistory.FindIndex(h => h.TradeDate == day.TradeDate);
                    // 获取前一个交易日的收盘价
                    decimal prevClose = dayIndex > 0 ? sortedHistory[dayIndex - 1].Close : day.Open;
                    decimal dayChange = day.Close - prevClose;
                    decimal dayChangePercent = prevClose != 0 ? (dayChange / prevClose) * 100 : 0;
                    
                    historyText += $"- {day.TradeDate:yyyy-MM-dd}: 开盘{day.Open:F2}元, 收盘{day.Close:F2}元, 最高{day.High:F2}元, 最低{day.Low:F2}元, 涨跌{dayChange:+#.##;-#.##;0}元({dayChangePercent:+#.##;-#.##;0}%), 成交量{day.Volume:F0}手, 成交额{day.Turnover:F2}万元\n";
                }
                
                historyText += $@"

**提示：请根据以上历史交易数据，结合当前价格和基本面信息，分析该股票的价格走势，并给出未来可能的走势预测。重点关注：**
1. 价格趋势是否与成交量变化一致
2. 最近的价格波动是否有异常
3. 结合基本面数据，判断当前价格是否合理
4. 基于历史走势，预测未来1-2周可能的股价走势
";
                }
                
                Console.WriteLine($"[AI分析] ✅ 已格式化历史交易数据，数据长度: {historyText.Length} 字符");
                _logger.LogInformation("🤖 [AIController] ✅ 已格式化历史交易数据，长度: {Length} 字符", historyText.Length);
            }
            
            if (fundamentalInfo != null)
            {
                Console.WriteLine($"[AI分析] 步骤4: 构建包含基本面信息的分析上下文...");
                _logger.LogInformation("🤖 [AIController] 步骤4: 构建包含基本面信息的分析上下文");
                
                var dataSourceNote = !string.IsNullOrEmpty(dataSource) ? $"（数据来源：{dataSource}）" : "";
                var fundamentalText = $@"

【最新财务数据】{dataSourceNote}（报告期：{fundamentalInfo.ReportDate ?? "未知"}，报告类型：{fundamentalInfo.ReportType ?? "未知"}）

**主要财务指标：**
- 营业收入：{(fundamentalInfo.TotalRevenue.HasValue ? fundamentalInfo.TotalRevenue.Value.ToString("F2") + "万元" : "N/A")}
- 净利润：{(fundamentalInfo.NetProfit.HasValue ? fundamentalInfo.NetProfit.Value.ToString("F2") + "万元" : "N/A")}
- 每股收益(EPS)：{(fundamentalInfo.EPS.HasValue ? fundamentalInfo.EPS.Value.ToString("F3") + "元" : "N/A")}
- 每股净资产(BPS)：{(fundamentalInfo.BPS.HasValue ? fundamentalInfo.BPS.Value.ToString("F3") + "元" : "N/A")}

**盈利能力：**
- 净资产收益率(ROE)：{(fundamentalInfo.ROE.HasValue ? fundamentalInfo.ROE.Value.ToString("F2") + "%" : "N/A")}
- 毛利率：{(fundamentalInfo.GrossProfitMargin.HasValue ? fundamentalInfo.GrossProfitMargin.Value.ToString("F2") + "%" : "N/A")}
- 净利率：{(fundamentalInfo.NetProfitMargin.HasValue ? fundamentalInfo.NetProfitMargin.Value.ToString("F2") + "%" : "N/A")}

**成长性：**
- 营收增长率：{(fundamentalInfo.RevenueGrowthRate.HasValue ? fundamentalInfo.RevenueGrowthRate.Value.ToString("F2") + "%" : "N/A")}
- 净利润增长率：{(fundamentalInfo.ProfitGrowthRate.HasValue ? fundamentalInfo.ProfitGrowthRate.Value.ToString("F2") + "%" : "N/A")}

**偿债能力：**
- 资产负债率：{(fundamentalInfo.AssetLiabilityRatio.HasValue ? fundamentalInfo.AssetLiabilityRatio.Value.ToString("F2") + "%" : "N/A")}
- 流动比率：{(fundamentalInfo.CurrentRatio.HasValue ? fundamentalInfo.CurrentRatio.Value.ToString("F2") : "N/A")}
- 速动比率：{(fundamentalInfo.QuickRatio.HasValue ? fundamentalInfo.QuickRatio.Value.ToString("F2") : "N/A")}

**运营能力：**
- 存货周转率：{(fundamentalInfo.InventoryTurnover.HasValue ? fundamentalInfo.InventoryTurnover.Value.ToString("F2") : "N/A")}
- 应收账款周转率：{(fundamentalInfo.AccountsReceivableTurnover.HasValue ? fundamentalInfo.AccountsReceivableTurnover.Value.ToString("F2") : "N/A")}

**估值指标：**
- 市盈率(PE)：{(fundamentalInfo.PE.HasValue ? fundamentalInfo.PE.Value.ToString("F2") : stock?.PE?.ToString("F2") ?? "N/A")}
- 市净率(PB)：{(fundamentalInfo.PB.HasValue ? fundamentalInfo.PB.Value.ToString("F2") : stock?.PB?.ToString("F2") ?? "N/A")}
";
                
                enhancedContext = string.IsNullOrEmpty(enhancedContext) 
                    ? fundamentalText + historyText + pythonAnalysisText + tradeDataText
                    : enhancedContext + fundamentalText + historyText + pythonAnalysisText + tradeDataText;
                
                Console.WriteLine($"[AI分析] ✅ 已构建包含基本面信息和历史数据的上下文，上下文长度: {enhancedContext.Length} 字符");
                _logger.LogInformation("🤖 [AIController] ✅ 已构建包含基本面信息和历史数据的上下文，长度: {Length} 字符", enhancedContext.Length);
            }
            else if (stock != null)
            {
                Console.WriteLine($"[AI分析] ⚠️ 使用实时行情数据构建分析上下文（未获取到基本面数据）");
                _logger.LogInformation("🤖 [AIController] ⚠️ 使用实时行情数据构建分析上下文（未获取到基本面数据）");
                
                // 如果没有基本面数据，至少提供实时行情数据
                var stockInfo = $@"

**当前行情数据：**
- 当前价格：{stock.CurrentPrice:F2}元
- 涨跌幅：{stock.ChangePercent:F2}%
- 市盈率(PE)：{(stock.PE?.ToString("F2") ?? "N/A")}
- 市净率(PB)：{(stock.PB?.ToString("F2") ?? "N/A")}
- 换手率：{stock.TurnoverRate:F2}%
";
                enhancedContext = string.IsNullOrEmpty(enhancedContext) 
                    ? stockInfo + historyText + pythonAnalysisText + tradeDataText
                    : enhancedContext + stockInfo + historyText + pythonAnalysisText + tradeDataText;
            }
            else
            {
                Console.WriteLine($"[AI分析] ⚠️ 既未获取到基本面数据，也未获取到实时行情数据，将使用原始上下文");
                _logger.LogWarning("🤖 [AIController] ⚠️ 既未获取到基本面数据，也未获取到实时行情数据，将使用原始上下文");
                
                // 即使没有基本面和实时行情，也尝试添加历史数据
                if (!string.IsNullOrEmpty(historyText) || !string.IsNullOrEmpty(pythonAnalysisText) || !string.IsNullOrEmpty(tradeDataText))
                {
                    enhancedContext = string.IsNullOrEmpty(enhancedContext) 
                        ? historyText + pythonAnalysisText + tradeDataText
                        : enhancedContext + historyText + pythonAnalysisText + tradeDataText;
                }
            }
            
            Console.WriteLine($"[AI分析] 步骤5: 调用AI服务进行分析...");
            _logger.LogInformation("🤖 [AIController] 步骤5: 调用AI服务进行分析");
            
            var result = await _aiService.AnalyzeStockAsync(stockCode, request?.PromptId, enhancedContext, request?.ModelId);
            
            Console.WriteLine($"[AI分析] ✅ AI分析完成！结果长度: {result?.Length ?? 0} 字符");
            _logger.LogInformation("🤖 [AIController] ✅ AI分析完成，结果长度: {Length} 字符", result?.Length ?? 0);
            
            // 确保返回正确的响应格式
            if (string.IsNullOrEmpty(result))
            {
                _logger.LogWarning("🤖 [AIController] ⚠️ AI分析结果为空");
                return Ok("AI分析完成，但未返回结果。请检查AI服务配置。");
            }
            
            // 记录响应大小（用于调试）
            var responseSizeKB = (result.Length * 2) / 1024.0; // 估算JSON大小（UTF-8，每个中文字符约2字节）
            Console.WriteLine($"[AI分析] 📊 响应大小估算: {responseSizeKB:F2} KB");
            _logger.LogInformation("🤖 [AIController] 📊 响应大小估算: {SizeKB:F2} KB", responseSizeKB);
            
            // 如果响应太大，给出警告
            if (responseSizeKB > 500)
            {
                _logger.LogWarning("🤖 [AIController] ⚠️ 响应较大 ({SizeKB:F2} KB)，可能影响传输", responseSizeKB);
            }
            
            // 返回JSON格式，包含分析结果
            return Ok(new { 
                success = true, 
                analysis = result,
                length = result.Length,
                sizeKB = Math.Round(responseSizeKB, 2),
                timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AI分析] ❌ 分析过程中发生错误: {ex.Message}");
            Console.WriteLine($"[AI分析] 异常堆栈: {ex.StackTrace}");
            
            _logger.LogError(ex, "🤖 [AIController] ❌ 分析股票 {StockCode} 失败", stockCode);
            
            // 如果获取基本面数据失败，仍然尝试使用原有方式分析
            Console.WriteLine($"[AI分析] 尝试使用原始上下文进行降级分析...");
            _logger.LogInformation("🤖 [AIController] 尝试使用原始上下文进行降级分析");
            
            try
            {
                var result = await _aiService.AnalyzeStockAsync(stockCode, request?.PromptId, request?.Context, request?.ModelId);
                
                // 确保返回正确的响应格式
                if (string.IsNullOrEmpty(result))
                {
                    _logger.LogWarning("🤖 [AIController] ⚠️ 降级分析结果为空");
                    return Ok(new { 
                        success = false, 
                        analysis = "AI分析失败，请检查AI服务配置。",
                        error = ex.Message
                    });
                }
                
                return Ok(new { 
                    success = true, 
                    analysis = result,
                    length = result.Length,
                    timestamp = DateTime.Now
                });
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "🤖 [AIController] ❌ 降级分析也失败");
                return Ok(new { 
                    success = false, 
                    analysis = $"AI分析失败: {ex.Message}",
                    error = ex2.Message
                });
            }
        }
    }

    /// <summary>
    /// 聊天
    /// </summary>
    [HttpPost("chat")]
    public async Task<ActionResult<string>> Chat([FromBody] ChatRequest request)
    {
        var result = await _aiService.ChatAsync(request.Message, request.Context);
        return Ok(result);
    }

    /// <summary>
    /// 获取股票建议
    /// </summary>
    [HttpGet("recommend/{stockCode}")]
    public async Task<ActionResult<string>> GetRecommendation(string stockCode)
    {
        var result = await _aiService.GetStockRecommendationAsync(stockCode);
        return Ok(result);
    }
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? Context { get; set; }
}

public class AnalyzeRequest
{
    public int? PromptId { get; set; }
    public string? Context { get; set; }
    public int? ModelId { get; set; }
}

