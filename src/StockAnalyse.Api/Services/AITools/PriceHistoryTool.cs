using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services.AITools;

public class PriceHistoryTool : IAITool
{
    private readonly IStockDataService _stockDataService;

    public PriceHistoryTool(IStockDataService stockDataService)
    {
        _stockDataService = stockDataService;
    }

    public AIToolName Name => AIToolName.GetPriceHistory;

    public AiTool GetDefinition()
    {
        return new AiTool
        {
            Function = new AiFunction
            {
                Name = Name.ToToolName(),
                Description = "获取股票近期的历史价格走势数据。",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        stock_code = new { type = "string", description = "股票代码" },
                        days = new { type = "integer", description = "获取的天数，默认30天" }
                    },
                    required = new[] { "stock_code" }
                }
            }
        };
    }

    public async Task<string> ExecuteAsync(JObject args)
    {
        string stockCode = args["stock_code"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(stockCode)) return "错误: 缺少股票代码";
        
        int days = args["days"]?.ToObject<int>() ?? 30;
        var endDate = DateTime.Now;
        var startDate = endDate.AddDays(-days * 1.5);
        
        var history = await _stockDataService.GetDailyDataAsync(stockCode, startDate, endDate);
        var simplified = history.OrderBy(h => h.TradeDate).Select(h => new 
        {
            Date = h.TradeDate.ToString("yyyy-MM-dd"),
            h.Close, h.Volume
        });
        
        return JsonConvert.SerializeObject(simplified);
    }
}
