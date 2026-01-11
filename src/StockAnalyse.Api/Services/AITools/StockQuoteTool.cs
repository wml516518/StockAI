using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services.AITools;

public class StockQuoteTool : IAITool
{
    private readonly IStockDataService _stockDataService;

    public StockQuoteTool(IStockDataService stockDataService)
    {
        _stockDataService = stockDataService;
    }

    public AIToolName Name => AIToolName.GetStockQuote;

    public AiTool GetDefinition()
    {
        return new AiTool
        {
            Function = new AiFunction
            {
                Name = Name.ToToolName(),
                Description = "获取A股股票的实时行情数据，包括当前价格、涨跌幅、成交量等。",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        stock_code = new { type = "string", description = "股票代码，如 600519" }
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
        
        var quote = await _stockDataService.GetWatchlistRealTimeQuoteAsync(stockCode);
        if (quote == null) return "未查询到该股票行情";
        
        return JsonConvert.SerializeObject(new 
        { 
            quote.Code, quote.Name, quote.CurrentPrice, quote.ChangePercent, 
            quote.Volume, quote.Turnover, quote.HighPrice, quote.LowPrice 
        });
    }
}
