using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services.AITools;

public class MarketSentimentTool : IAITool
{
    private readonly IMarketService _marketService;

    public MarketSentimentTool(IMarketService marketService)
    {
        _marketService = marketService;
    }

    public AIToolName Name => AIToolName.GetMarketSentiment;

    public AiTool GetDefinition()
    {
        return new AiTool
        {
            Function = new AiFunction
            {
                Name = Name.ToToolName(),
                Description = "获取股票的市场人气排名和热度信息。",
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
        
        var hotRank = await _marketService.GetHotRankFromAKShareAsync(stockCode);
        return hotRank ?? "暂无市场热度数据";
    }
}
