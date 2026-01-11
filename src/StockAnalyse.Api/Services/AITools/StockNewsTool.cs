using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services.AITools;

public class StockNewsTool : IAITool
{
    private readonly INewsService _newsService;

    public StockNewsTool(INewsService newsService)
    {
        _newsService = newsService;
    }

    public AIToolName Name => AIToolName.GetStockNews;

    public AiTool GetDefinition()
    {
        return new AiTool
        {
            Function = new AiFunction
            {
                Name = Name.ToToolName(),
                Description = "获取股票相关的最近新闻资讯。",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        stock_code = new { type = "string", description = "股票代码" },
                        limit = new { type = "integer", description = "新闻条数，默认为3" }
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
        
        int limit = args["limit"]?.ToObject<int>() ?? 3;
        var news = await _newsService.GetNewsByStockAsync(stockCode);
        
        return JsonConvert.SerializeObject(news.OrderByDescending(n => n.PublishTime).Take(limit).Select(n => new
        {
            n.Title, n.PublishTime, Summary = n.Summary?.Substring(0, Math.Min(n.Summary.Length, 100))
        }));
    }
}
