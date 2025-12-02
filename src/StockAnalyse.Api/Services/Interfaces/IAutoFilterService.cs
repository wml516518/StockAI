using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

/// <summary>
/// 自动筛选股票服务接口
/// </summary>
public interface IAutoFilterService
{
    /// <summary>
    /// 自动筛选股票（综合基本面、技术面、公告新闻等条件）
    /// </summary>
    /// <param name="stockCodes">待筛选的股票代码列表（如果为空，则从全市场筛选）</param>
    /// <param name="enableSentimentFilter">是否启用社交舆情过滤（可选）</param>
    /// <returns>筛选后的股票代码列表及筛选原因</returns>
    Task<AutoFilterResult> FilterStocksAsync(
        List<string>? stockCodes = null,
        bool enableSentimentFilter = false);

    /// <summary>
    /// 检查股票是否符合基本面条件
    /// </summary>
    Task<FundamentalFilterResult> CheckFundamentalConditionsAsync(string stockCode);

    /// <summary>
    /// 检查股票是否符合技术面条件
    /// </summary>
    Task<TechnicalFilterResult> CheckTechnicalConditionsAsync(string stockCode);

    /// <summary>
    /// 检查股票公告和新闻是否有负面信息
    /// </summary>
    Task<NewsFilterResult> CheckNewsConditionsAsync(string stockCode);
}

/// <summary>
/// 自动筛选结果
/// </summary>
public class AutoFilterResult
{
    /// <summary>
    /// 通过筛选的股票代码列表
    /// </summary>
    public List<string> PassedStockCodes { get; set; } = new();

    /// <summary>
    /// 被过滤的股票及原因
    /// </summary>
    public Dictionary<string, string> FilteredReasons { get; set; } = new();

    /// <summary>
    /// 筛选统计信息
    /// </summary>
    public FilterStatistics Statistics { get; set; } = new();
}

/// <summary>
/// 筛选统计信息
/// </summary>
public class FilterStatistics
{
    public int TotalChecked { get; set; }
    public int PassedFundamental { get; set; }
    public int PassedTechnical { get; set; }
    public int PassedNews { get; set; }
    public int PassedAll { get; set; }
    public int FilteredByFundamental { get; set; }
    public int FilteredByTechnical { get; set; }
    public int FilteredByNews { get; set; }

    // 基本面详细统计
    public int FilteredByFundamentalDataMissing { get; set; } // 无法获取基本面数据
    public int FilteredByNetProfit { get; set; } // 净利润<=0
    public int FilteredByROE { get; set; } // ROE<=6%
    public int FilteredByGrossProfitMargin { get; set; } // 毛利率<=15%
    public int FilteredByRevenueGrowth { get; set; } // 营收同比<=0
    public int FilteredByST { get; set; } // ST类股票
    public int FilteredByAssetLiabilityRatio { get; set; } // 资产负债率>=70%
    public int FilteredByCurrentRatio { get; set; } // 流动比率<=1
    public int FilteredByIndustry { get; set; } // 行业不符合
    
    /// <summary>
    /// 被过滤的行业统计（行业名称 -> 数量）
    /// </summary>
    public Dictionary<string, int> FilteredIndustries { get; set; } = new();

    // 技术面详细统计
    public int FilteredByTechnicalDataMissing { get; set; } // 无法获取技术面数据
    public int FilteredByReversalSignal { get; set; } // 未满足反转信号
    public int FilteredByVolume { get; set; } // 成交量不足
    public int FilteredByTurnoverRate { get; set; } // 换手率不在2%-12%
    public int FilteredByChangePercent { get; set; } // 涨幅>7%
    public int FilteredByVolatility { get; set; } // 波动率>=6%

    // 新闻筛选详细统计
    public int FilteredByNewsNegative { get; set; } // 发现负面新闻
}

/// <summary>
/// 基本面筛选失败原因类型
/// </summary>
public enum FundamentalFilterFailureType
{
    None, // 通过
    DataMissing, // 无法获取基本面数据
    NetProfit, // 净利润<=0
    ROE, // ROE<=6%
    GrossProfitMargin, // 毛利率<=15%
    RevenueGrowth, // 营收同比<=0
    ST, // ST类股票
    AssetLiabilityRatio, // 资产负债率>=70%
    CurrentRatio, // 流动比率<=1
    Industry, // 行业不符合
    Exception // 检查异常
}

/// <summary>
/// 技术面筛选失败原因类型
/// </summary>
public enum TechnicalFilterFailureType
{
    None, // 通过
    DataMissing, // 无法获取技术面数据
    ReversalSignal, // 未满足反转信号
    Volume, // 成交量不足
    TurnoverRate, // 换手率不在2%-12%
    ChangePercent, // 涨幅>7%
    Volatility, // 波动率>=6%
    Exception // 检查异常
}

/// <summary>
/// 基本面筛选结果
/// </summary>
public class FundamentalFilterResult
{
    public bool Passed { get; set; }
    public string? Reason { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
    public FundamentalFilterFailureType FailureType { get; set; } = FundamentalFilterFailureType.None;
}

/// <summary>
/// 技术面筛选结果
/// </summary>
public class TechnicalFilterResult
{
    public bool Passed { get; set; }
    public string? Reason { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
    public TechnicalFilterFailureType FailureType { get; set; } = TechnicalFilterFailureType.None;
}

/// <summary>
/// 新闻筛选结果
/// </summary>
public class NewsFilterResult
{
    public bool Passed { get; set; }
    public string? Reason { get; set; }
    public List<string> NegativeKeywords { get; set; } = new();
}

