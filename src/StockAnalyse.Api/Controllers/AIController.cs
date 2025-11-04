using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Services.Interfaces;
using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<AIController> _logger;

    public AIController(IAIService aiService, IStockDataService stockDataService, ILogger<AIController> logger)
    {
        _aiService = aiService;
        _stockDataService = stockDataService;
        _logger = logger;
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
            
            // 构建包含基本面信息的上下文
            string? enhancedContext = request?.Context;
            
            if (fundamentalInfo != null)
            {
                Console.WriteLine($"[AI分析] 步骤3: 构建包含基本面信息的分析上下文...");
                _logger.LogInformation("🤖 [AIController] 步骤3: 构建包含基本面信息的分析上下文");
                
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
                    ? fundamentalText 
                    : enhancedContext + fundamentalText;
                
                Console.WriteLine($"[AI分析] ✅ 已构建包含基本面信息的上下文，上下文长度: {enhancedContext.Length} 字符");
                _logger.LogInformation("🤖 [AIController] ✅ 已构建包含基本面信息的上下文，长度: {Length} 字符", enhancedContext.Length);
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
                    ? stockInfo 
                    : enhancedContext + stockInfo;
            }
            else
            {
                Console.WriteLine($"[AI分析] ⚠️ 既未获取到基本面数据，也未获取到实时行情数据，将使用原始上下文");
                _logger.LogWarning("🤖 [AIController] ⚠️ 既未获取到基本面数据，也未获取到实时行情数据，将使用原始上下文");
            }
            
            Console.WriteLine($"[AI分析] 步骤4: 调用AI服务进行分析...");
            _logger.LogInformation("🤖 [AIController] 步骤4: 调用AI服务进行分析");
            
            var result = await _aiService.AnalyzeStockAsync(stockCode, request?.PromptId, enhancedContext, request?.ModelId);
            
            Console.WriteLine($"[AI分析] ✅ AI分析完成！结果长度: {result?.Length ?? 0} 字符");
            _logger.LogInformation("🤖 [AIController] ✅ AI分析完成，结果长度: {Length} 字符", result?.Length ?? 0);
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AI分析] ❌ 分析过程中发生错误: {ex.Message}");
            Console.WriteLine($"[AI分析] 异常堆栈: {ex.StackTrace}");
            
            _logger.LogError(ex, "🤖 [AIController] ❌ 分析股票 {StockCode} 失败", stockCode);
            
            // 如果获取基本面数据失败，仍然尝试使用原有方式分析
            Console.WriteLine($"[AI分析] 尝试使用原始上下文进行降级分析...");
            _logger.LogInformation("🤖 [AIController] 尝试使用原始上下文进行降级分析");
            
            var result = await _aiService.AnalyzeStockAsync(stockCode, request?.PromptId, request?.Context, request?.ModelId);
            return Ok(result);
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

