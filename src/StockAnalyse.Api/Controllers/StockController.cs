using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StockController : ControllerBase
{
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<StockController> _logger;

    public StockController(IStockDataService stockDataService, ILogger<StockController> logger)
    {
        _stockDataService = stockDataService;
        _logger = logger;
    }

    /// <summary>
    /// 获取股票实时行情
    /// </summary>
    [HttpGet("{code}")]
    public async Task<ActionResult<Stock>> GetStock(string code)
    {
        var stock = await _stockDataService.GetRealTimeQuoteAsync(code);
        if (stock == null)
        {
            return NotFound();
        }
        return Ok(stock);
    }

    /// <summary>
    /// 批量获取股票行情
    /// </summary>
    [HttpPost("batch")]
    public async Task<ActionResult<List<Stock>>> GetBatchStocks([FromBody] List<string> codes)
    {
        var stocks = await _stockDataService.GetBatchQuotesAsync(codes);
        return Ok(stocks);
    }

    /// <summary>
    /// 获取日线数据
    /// </summary>
    [HttpGet("{code}/history")]
    public async Task<ActionResult<List<StockHistory>>> GetHistory(string code, DateTime? startDate, DateTime? endDate)
    {
        var start = startDate ?? DateTime.Now.AddDays(-30);
        var end = endDate ?? DateTime.Now;
        
        var histories = await _stockDataService.GetDailyDataAsync(code, start, end);
        return Ok(histories);
    }

    /// <summary>
    /// 测试接口：获取股票原始API数据（用于调试字段映射）
    /// </summary>
    [HttpGet("{code}/test-raw-data")]
    public async Task<ActionResult<object>> TestRawData(string code, DateTime? startDate, DateTime? endDate)
    {
        var start = startDate ?? DateTime.Now.AddDays(-7);
        var end = endDate ?? DateTime.Now;
        
        try
        {
            var market = code.StartsWith("6") ? "1" : "0";
            var secid = $"{market}.{code}";
            var beg = start.ToString("yyyyMMdd");
            var endStr = end.ToString("yyyyMMdd");

            var url = $"http://push2his.eastmoney.com/api/qt/stock/kline/get?secid={secid}&fields1=f1,f2,f3,f4&fields2=f51,f52,f53,f54,f55,f56,f57&klt=101&fqt=1&beg={beg}&end={endStr}";
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            httpClient.DefaultRequestHeaders.Add("Referer", "http://quote.eastmoney.com/");
            
            var response = await httpClient.GetStringAsync(url);
            dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
            
            if (data?.data?.klines == null)
            {
                return Ok(new { success = false, message = "API返回数据为空", rawResponse = response });
            }

            var rawLines = new List<object>();
            var parsedData = new List<object>();
            
            int count = 0;
            foreach (var k in data.data.klines)
            {
                if (count >= 5) break; // 只返回前5条数据
                
                string line = k.ToString();
                var parts = line.Split(',');
                
                rawLines.Add(new
                {
                    rawLine = line,
                    parts = parts,
                    partsCount = parts.Length
                });
                
                if (parts.Length >= 7)
                {
                    parsedData.Add(new
                    {
                        日期 = parts[0],
                        parts1_当前解析为收盘 = parts[1],
                        parts2_当前解析为最高 = parts[2],
                        parts3_当前解析为最低 = parts[3],
                        parts4_当前解析为开盘 = parts[4],
                        parts5_成交量 = parts[5],
                        parts6_成交额 = parts[6],
                        说明 = "请告诉我parts[1]到parts[4]实际对应的是什么价格"
                    });
                }
                
                count++;
            }
            
            return Ok(new
            {
                success = true,
                stockCode = code,
                dateRange = $"{start:yyyy-MM-dd} 到 {end:yyyy-MM-dd}",
                totalRecords = ((System.Collections.ICollection)data.data.klines).Count,
                rawLines = rawLines,
                parsedData = parsedData,
                note = "请查看parsedData中的parts[1]到parts[4]的值，告诉我它们分别对应：开盘、收盘、最高、最低中的哪一个"
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// 获取排名列表
    /// </summary>
    [HttpGet("ranking/{market}")]
    public async Task<ActionResult<List<Stock>>> GetRanking(string market, int top = 100)
    {
        var stocks = await _stockDataService.GetRankingListAsync(market, top);
        return Ok(stocks);
    }

    /// <summary>
    /// 计算MACD指标（注意：不再更新数据库中的MACD字段）
    /// </summary>
    [HttpPost("{code}/macd")]
    public async Task<ActionResult<object>> CalculateMACD(string code)
    {
        var result = await _stockDataService.CalculateMACDAsync(code);
        return Ok(new { Macd = result.macd, Signal = result.signal, Histogram = result.histogram });
    }

    /// <summary>
    /// 获取股票基本面信息
    /// </summary>
    [HttpGet("{code}/fundamental")]
    public async Task<ActionResult<StockFundamentalInfo>> GetFundamental(string code)
    {
        var info = await _stockDataService.GetFundamentalInfoAsync(code);
        if (info == null)
        {
            return NotFound();
        }
        return Ok(info);
    }

    /// <summary>
    /// 验证历史数据的完整性和可靠性
    /// </summary>
    [HttpGet("{code}/history/validate")]
    public async Task<ActionResult<object>> ValidateHistoryData(string code, DateTime? startDate, DateTime? endDate, int? months = 3)
    {
        var end = endDate ?? DateTime.Now;
        var start = startDate ?? end.AddMonths(-(months ?? 3));
        
        // 计算理论交易日数量
        int totalDays = (int)(end - start).TotalDays;
        int weekendDays = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                weekendDays++;
        }
        int theoreticalTradingDays = totalDays - weekendDays;
        
        // 获取实际数据
        var histories = await _stockDataService.GetDailyDataAsync(code, start, end);
        
        if (histories.Count == 0)
        {
            return Ok(new
            {
                StockCode = code,
                StartDate = start,
                EndDate = end,
                ActualCount = 0,
                TheoreticalCount = theoreticalTradingDays,
                CompletenessRatio = 0.0,
                Status = "无数据",
                Message = "未找到历史数据，建议从API拉取",
                MissingDates = new List<string>()
            });
        }
        
        // 检查数据连续性
        var sortedHistory = histories.OrderBy(h => h.TradeDate).ToList();
        var firstDate = sortedHistory.First().TradeDate;
        var lastDate = sortedHistory.Last().TradeDate;
        var dateSet = sortedHistory.Select(h => h.TradeDate.Date).ToHashSet();
        
        var missingDates = new List<DateTime>();
        for (var date = firstDate.Date; date <= lastDate.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                if (!dateSet.Contains(date))
                {
                    missingDates.Add(date);
                }
            }
        }
        
        double completenessRatio = histories.Count * 100.0 / theoreticalTradingDays;
        string status;
        string message;
        
        if (completenessRatio >= 85)
        {
            status = "优秀";
            message = $"数据完整度{completenessRatio:F1}%，数据可靠性高";
        }
        else if (completenessRatio >= 70)
        {
            status = "良好";
            message = $"数据完整度{completenessRatio:F1}%，可能缺少部分交易日数据（节假日、停牌等）";
        }
        else
        {
            status = "不足";
            message = $"数据完整度{completenessRatio:F1}%，建议检查数据源或重新拉取数据";
        }
        
        return Ok(new
        {
            StockCode = code,
            StartDate = start,
            EndDate = end,
            ActualCount = histories.Count,
            TheoreticalCount = theoreticalTradingDays,
            CompletenessRatio = Math.Round(completenessRatio, 2),
            Status = status,
            Message = message,
            FirstTradeDate = firstDate,
            LastTradeDate = lastDate,
            MissingDates = missingDates.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
            MissingCount = missingDates.Count,
            DataQuality = completenessRatio >= 85 ? "优秀" : completenessRatio >= 70 ? "良好" : "需改进"
        });
    }
}

