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
    public string Name { get; set; } = string.Empty;          // 提示词名称，如"基本面分析"
    public string? Description { get; set; }                  // 提示词描述
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

/// <summary>
/// 选股条件模板
/// </summary>
[Table("ScreenTemplates")]
public class ScreenTemplate
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty; // 模板名称
    
    [MaxLength(500)]
    public string? Description { get; set; } // 模板描述
    
    // 价格条件
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    
    // 涨跌幅条件
    public decimal? MinChangePercent { get; set; }
    public decimal? MaxChangePercent { get; set; }
    
    // 换手率条件
    public decimal? MinTurnoverRate { get; set; }
    public decimal? MaxTurnoverRate { get; set; }
    
    // 成交量条件
    public decimal? MinVolume { get; set; }
    public decimal? MaxVolume { get; set; }
    
    // 市值条件（万元）
    public decimal? MinMarketValue { get; set; }
    public decimal? MaxMarketValue { get; set; }
    
    // 基本面条件
    public decimal? MinPE { get; set; }
    public decimal? MaxPE { get; set; }
    public decimal? MinPB { get; set; }
    public decimal? MaxPB { get; set; }
    
    // 股息率条件
    public decimal? MinDividendYield { get; set; }
    public decimal? MaxDividendYield { get; set; }
    
    // 流通股本条件（万股）
    public decimal? MinCirculatingShares { get; set; }
    public decimal? MaxCirculatingShares { get; set; }
    
    // 总股本条件（万股）
    public decimal? MinTotalShares { get; set; }
    public decimal? MaxTotalShares { get; set; }
    
    // 市场筛选
    [MaxLength(10)]
    public string? Market { get; set; } // SH/SZ
    
    public DateTime CreateTime { get; set; } = DateTime.Now;
    public DateTime UpdateTime { get; set; } = DateTime.Now;
    public bool IsDefault { get; set; } = false; // 是否为默认模板
}

/// <summary>
/// AI配置
/// </summary>
public class AiConfig
{
    public string SystemPrompt { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
}

/// <summary>
/// 分页响应模型
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;
}

/// <summary>
/// 选股条件
/// </summary>
public class ScreenCriteria
{
    public string? Market { get; set; } // 市场：SH/SZ
    
    // 分页参数
    public int PageIndex { get; set; } = 1; // 页码，从1开始
    public int PageSize { get; set; } = 20; // 每页数量
    
    // 价格条件
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    
    // 涨跌幅条件
    public decimal? MinChangePercent { get; set; }
    public decimal? MaxChangePercent { get; set; }
    
    // 换手率条件
    public decimal? MinTurnoverRate { get; set; }
    public decimal? MaxTurnoverRate { get; set; }
    
    // 成交量条件
    public decimal? MinVolume { get; set; }
    public decimal? MaxVolume { get; set; }
    
    // 市值条件（万元）
    public decimal? MinMarketValue { get; set; }
    public decimal? MaxMarketValue { get; set; }
    
    // 基本面条件
    public decimal? MinPE { get; set; }
    public decimal? MaxPE { get; set; }
    public decimal? MinPB { get; set; }
    public decimal? MaxPB { get; set; }
    
    // 股息率条件
    public decimal? MinDividendYield { get; set; }
    public decimal? MaxDividendYield { get; set; }
    
    // 流通股本条件（万股）
    public decimal? MinCirculatingShares { get; set; }
    public decimal? MaxCirculatingShares { get; set; }
    
    // 总股本条件（万股）
    public decimal? MinTotalShares { get; set; }
    public decimal? MaxTotalShares { get; set; }
}

/// <summary>
/// 策略类型枚举
/// </summary>
public enum StrategyType
{
    TechnicalIndicator, // 技术指标策略
    Fundamental, // 基本面策略
    Arbitrage, // 套利策略
    MachineLearning, // 机器学习策略
    Custom // 自定义策略
}

/// <summary>
/// 信号类型枚举
/// </summary>
public enum SignalType
{
    Buy, // 买入信号
    Sell, // 卖出信号
    Hold // 持有信号
}

/// <summary>
/// 交易类型枚举
/// </summary>
public enum TradeType
{
    Buy, // 买入
    Sell // 卖出
}

/// <summary>
/// 量化策略模型
/// </summary>
[Table("QuantStrategies")]
public class QuantStrategy
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty; // 策略名称
    
    [MaxLength(500)]
    public string? Description { get; set; } // 策略描述
    
    public StrategyType Type { get; set; } // 策略类型
    
    [Column(TypeName = "TEXT")]
    public string Parameters { get; set; } = "{}"; // JSON格式存储参数
    
    public bool IsActive { get; set; } = true; // 是否激活
    
    public decimal InitialCapital { get; set; } = 100000; // 初始资金
    
    public decimal CurrentCapital { get; set; } = 100000; // 当前资金
    
    public DateTime CreatedAt { get; set; } = DateTime.Now; // 创建时间
    
    public DateTime UpdatedAt { get; set; } = DateTime.Now; // 更新时间
    
    public DateTime? LastRunAt { get; set; } // 最后运行时间
    
    // 导航属性
    public virtual List<TradingSignal> TradingSignals { get; set; } = new();
    public virtual List<SimulatedTrade> SimulatedTrades { get; set; } = new();
    public virtual List<BacktestResult> BacktestResults { get; set; } = new();
}

/// <summary>
/// 交易信号模型
/// </summary>
[Table("TradingSignals")]
public class TradingSignal
{
    [Key]
    public int Id { get; set; }
    
    public int StrategyId { get; set; } // 策略ID
    
    [Required]
    [MaxLength(10)]
    public string StockCode { get; set; } = string.Empty; // 股票代码
    
    public SignalType Type { get; set; } // 信号类型
    
    public decimal Price { get; set; } // 信号价格
    
    public decimal Confidence { get; set; } = 0.5m; // 信号置信度 (0-1)
    
    [MaxLength(500)]
    public string? Reason { get; set; } // 信号原因
    
    public DateTime GeneratedAt { get; set; } = DateTime.Now; // 生成时间
    
    public bool IsExecuted { get; set; } = false; // 是否已执行
    
    public DateTime? ExecutedAt { get; set; } // 执行时间
    
    // 导航属性
    public virtual QuantStrategy Strategy { get; set; } = null!;
}

/// <summary>
/// 模拟交易记录模型
/// </summary>
[Table("SimulatedTrades")]
public class SimulatedTrade
{
    [Key]
    public int Id { get; set; }
    
    public int StrategyId { get; set; } // 策略ID
    
    [Required]
    [MaxLength(10)]
    public string StockCode { get; set; } = string.Empty; // 股票代码
    
    public TradeType Type { get; set; } // 交易类型
    
    public decimal Quantity { get; set; } // 交易数量
    
    public decimal Price { get; set; } // 交易价格
    
    public decimal Commission { get; set; } = 0; // 手续费
    
    public decimal Amount { get; set; } // 交易金额
    
    public DateTime ExecutedAt { get; set; } = DateTime.Now; // 执行时间
    
    [MaxLength(200)]
    public string? Notes { get; set; } // 备注
    
    // 导航属性
    public virtual QuantStrategy Strategy { get; set; } = null!;
}

/// <summary>
/// 回测结果模型
/// </summary>
[Table("BacktestResults")]
public class BacktestResult
{
    [Key]
    public int Id { get; set; }
    
    public int StrategyId { get; set; } // 策略ID
    
    public DateTime StartDate { get; set; } // 回测开始日期
    
    public DateTime EndDate { get; set; } // 回测结束日期
    
    public decimal InitialCapital { get; set; } // 初始资金
    
    public decimal FinalCapital { get; set; } // 最终资金
    
    public decimal TotalReturn { get; set; } // 总收益率
    
    public decimal AnnualizedReturn { get; set; } // 年化收益率
    
    public decimal SharpeRatio { get; set; } // 夏普比率
    
    public decimal MaxDrawdown { get; set; } // 最大回撤
    
    public int TotalTrades { get; set; } // 总交易次数
    
    public int WinningTrades { get; set; } // 盈利交易次数
    
    public decimal WinRate { get; set; } // 胜率
    
    public DateTime CreatedAt { get; set; } = DateTime.Now; // 创建时间
    
    [Column(TypeName = "TEXT")]
    public string? DetailedResults { get; set; } // JSON格式存储详细结果
    
    // 导航属性
    public virtual QuantStrategy Strategy { get; set; } = null!;
}

/// <summary>
/// 技术指标参数模型
/// </summary>
public class TechnicalIndicatorParameters
{
    // 移动平均线参数
    public int ShortPeriod { get; set; } = 5; // 短期周期
    public int LongPeriod { get; set; } = 20; // 长期周期
    
    // MACD参数
    public int FastPeriod { get; set; } = 12; // 快线周期
    public int SlowPeriod { get; set; } = 26; // 慢线周期
    public int SignalPeriod { get; set; } = 9; // 信号线周期
    
    // RSI参数
    public int RSIPeriod { get; set; } = 14; // RSI周期
    public decimal RSIOverBought { get; set; } = 70; // 超买线
    public decimal RSIOverSold { get; set; } = 30; // 超卖线
    
    // 布林带参数
    public int BollingerPeriod { get; set; } = 20; // 布林带周期
    public decimal BollingerStdDev { get; set; } = 2; // 标准差倍数
}

/// <summary>
/// 策略优化结果模型
/// </summary>
[Table("StrategyOptimizationResults")]
public class StrategyOptimizationResult
{
    [Key]
    public int Id { get; set; }
    
    public int StrategyId { get; set; } // 策略ID
    
    [Column(TypeName = "TEXT")]
    public string OptimizedParameters { get; set; } = "{}"; // JSON格式存储优化后的参数
    
    [Column(TypeName = "TEXT")]
    public string OptimizationConfig { get; set; } = "{}"; // JSON格式存储优化配置
    
    // 优化结果指标
    public decimal TotalReturn { get; set; } // 总收益率
    public decimal SharpeRatio { get; set; } // 夏普比率
    public decimal MaxDrawdown { get; set; } // 最大回撤
    public decimal WinRate { get; set; } // 胜率
    public int TotalTrades { get; set; } // 总交易次数
    
    // 优化过程信息
    public int TotalCombinations { get; set; } // 总参数组合数
    public int TestedCombinations { get; set; } // 已测试组合数
    public TimeSpan OptimizationDuration { get; set; } // 优化耗时
    
    public DateTime StartDate { get; set; } // 回测开始日期
    public DateTime EndDate { get; set; } // 回测结束日期
    
    [Column(TypeName = "TEXT")]
    public string StockCodes { get; set; } = "[]"; // JSON格式存储股票代码列表
    
    public DateTime CreatedAt { get; set; } = DateTime.Now; // 创建时间
    
    public bool IsApplied { get; set; } = false; // 是否已应用到策略
    
    // 导航属性
    public virtual QuantStrategy Strategy { get; set; } = null!;
}

/// <summary>
/// 参数组合测试结果
/// </summary>
[Table("ParameterTestResults")]
public class ParameterTestResult
{
    [Key]
    public int Id { get; set; }
    
    public int OptimizationResultId { get; set; } // 优化结果ID
    
    [Column(TypeName = "TEXT")]
    public string Parameters { get; set; } = "{}"; // JSON格式存储参数组合
    
    public decimal TotalReturn { get; set; } // 总收益率
    public decimal SharpeRatio { get; set; } // 夏普比率
    public decimal MaxDrawdown { get; set; } // 最大回撤
    public decimal WinRate { get; set; } // 胜率
    public int TotalTrades { get; set; } // 总交易次数
    
    public DateTime TestedAt { get; set; } = DateTime.Now; // 测试时间
    
    // 导航属性
    public virtual StrategyOptimizationResult OptimizationResult { get; set; } = null!;
}

