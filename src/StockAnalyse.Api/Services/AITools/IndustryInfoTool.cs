using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services.AITools;

public class IndustryInfoTool : IAITool
{
    private readonly IIndustryService _industryService;

    public IndustryInfoTool(IIndustryService industryService)
    {
        _industryService = industryService;
    }

    public AIToolName Name => AIToolName.GetIndustryInfo;

    public AiTool GetDefinition()
    {
        return new AiTool
        {
            Function = new AiFunction
            {
                Name = Name.ToToolName(),
                Description = "获取股票所属行业信息及行业详情。",
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
        
        var result = await _industryService.GetIndustryInfoFromAKShareAsync(stockCode);
        if (result == null) return "未获取到行业信息";
        
        return JsonConvert.SerializeObject(new { result.IndustryName, result.InfoText });
    }
}
