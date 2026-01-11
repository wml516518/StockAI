using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services.AITools;

public class StockFundamentalsTool : IAITool
{
    private readonly IStockDataService _stockDataService;

    public StockFundamentalsTool(IStockDataService stockDataService)
    {
        _stockDataService = stockDataService;
    }

    public AIToolName Name => AIToolName.GetStockFundamentals;

    public AiTool GetDefinition()
    {
        return new AiTool
        {
            Function = new AiFunction
            {
                Name = Name.ToToolName(),
                Description = "获取股票的基本面财务数据，如营收、利润、PE、ROE等。",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        stock_code = new { type = "string", description = "股票代码" }
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
        
        var info = await _stockDataService.GetFundamentalInfoAsync(stockCode);
        if (info == null) return "未获取到基本面数据";
        
        return JsonConvert.SerializeObject(new 
        { 
            info.ReportDate, info.ReportType, 
            info.TotalRevenue, info.NetProfit, info.EPS, info.ROE, 
            info.GrossProfitMargin, info.NetProfitMargin,
            info.PE, info.PB, info.AssetLiabilityRatio
        });
    }
}
