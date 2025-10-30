using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalyse.Api.Models;

/// <summary>
/// 股票基础信息
/// </summary>
[Table("Stocks")]
public class Stock
{
    [Key]
    public string Code { get; set; } = string.Empty; // 股票代码
    public string Name { get; set; } = string.Empty; // 股票名称
    public string Market { get; set; } = string.Empty; // 市场（SH/SZ）
    
    // 实时行情
    public decimal CurrentPrice { get; set; } // 当前价
    public decimal OpenPrice { get; set; } // 开盘价
    public decimal ClosePrice { get; set; } // 昨收价
    public decimal HighPrice { get; set; } // 最高价
    public decimal LowPrice { get; set; } // 最低价
    public decimal Volume { get; set; } // 成交量
    public decimal Turnover { get; set; } // 成交额
    public decimal ChangePercent { get; set; } // 涨跌幅(%)
    public decimal ChangeAmount { get; set; } // 涨跌额
    
    // 技术指标
    public decimal TurnoverRate { get; set; } // 换手率(%)
    
    // 基本面
    public decimal? PE { get; set; } // 市盈率（可为空）
    public decimal? PB { get; set; } // 市净率（可为空）
    
    public DateTime LastUpdate { get; set; } // 最后更新时间
    
    // 导航属性
    public virtual List<WatchlistStock> Watchlists { get; set; } = new();
    public virtual List<StockHistory> Histories { get; set; } = new();
}

/// <summary>
/// 自选股
/// </summary>
[Table("WatchlistStocks")]
public class WatchlistStock
{
    [Key]
    public int Id { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public int WatchlistCategoryId { get; set; }
    
    // 成本信息
    public decimal? CostPrice { get; set; } // 成本价
    public decimal? Quantity { get; set; } // 持仓数量
    public decimal TotalCost { get; set; } // 总成本
    public decimal ProfitLoss { get; set; } // 盈亏
    public decimal ProfitLossPercent { get; set; } // 盈亏百分比
    
    // 价格提醒
    public decimal? HighAlertPrice { get; set; } // 上涨提醒价
    public decimal? LowAlertPrice { get; set; } // 下跌提醒价
    public bool HighAlertSent { get; set; }
    public bool LowAlertSent { get; set; }
    
    public DateTime AddTime { get; set; }
    public DateTime LastUpdate { get; set; }
    
    // 导航属性
    public virtual Stock? Stock { get; set; }
    public virtual WatchlistCategory? Category { get; set; }
}

/// <summary>
/// 自选股分类
/// </summary>
[Table("WatchlistCategories")]
public class WatchlistCategory
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // 分类名称（已购、预购等）
    public string? Description { get; set; }
    public string Color { get; set; } = "#1890ff"; // 颜色
    public int SortOrder { get; set; }
    
    public virtual List<WatchlistStock> Stocks { get; set; } = new();
}

/// <summary>
/// 股票历史数据
/// </summary>
[Table("StockHistories")]
public class StockHistory
{
    [Key]
    public int Id { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public DateTime TradeDate { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public decimal Turnover { get; set; }
    
    // 导航属性
    public virtual Stock? Stock { get; set; }
}

/// <summary>
/// 金融消息
/// </summary>
[Table("FinancialNews")]
public class FinancialNews
{
    [Key]
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // 来源（财联社、新浪财经等）
    public string? Url { get; set; }
    public DateTime PublishTime { get; set; }
    public List<string>? StockCodes { get; set; } // 相关股票代码
    public int ViewCount { get; set; }
    public DateTime FetchTime { get; set; }
}

/// <summary>
/// 大模型配置
/// </summary>
[Table("AIModelConfigs")]
public class AIModelConfig
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // DeepSeek、OpenAI等
    public string ApiKey { get; set; } = string.Empty;
    public string SubscribeEndpoint { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// AI提示词（支持多条，便于在分析时选择）
/// </summary>
[Table("AIPrompts")]
public class AIPrompt
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;          // 提示词名称，如“基本面分析”
    public string SystemPrompt { get; set; } = string.Empty;  // 系统提示词
    public double Temperature { get; set; } = 0.7;            // 采样温度
    public bool IsDefault { get; set; }                       // 是否默认提示词
    public bool IsActive { get; set; } = true;                // 是否启用
}

/// <summary>
/// 涨跌幅提醒通知
/// </summary>
[Table("PriceAlerts")]
public class PriceAlert
{
    [Key]
    public int Id { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public decimal TargetPrice { get; set; }
    public AlertType Type { get; set; } // 上涨/下跌/到达价格
    public bool IsTriggered { get; set; }
    public DateTime? TriggerTime { get; set; }
    public DateTime CreateTime { get; set; }
    public string? Note { get; set; }
    public bool IsTriggerPercent { get; set; } // 是否按百分比触发
}

public enum AlertType
{
    PriceUp, // 价格上涨
    PriceDown, // 价格下跌
    PriceReach // 到达价格
}

