using System.Text.Json.Serialization;

namespace StockAnalyse.Api.Models;

/// <summary>
/// 策略配置模型
/// </summary>
public class StrategyConfig
{
    /// <summary>
    /// 策略名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 策略描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 策略类型
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StrategyType Type { get; set; }

    /// <summary>
    /// 技术指标参数
    /// </summary>
    public TechnicalIndicatorParameters Parameters { get; set; } = new();

    /// <summary>
    /// 初始资金
    /// </summary>
    public decimal InitialCapital { get; set; } = 100000;

    /// <summary>
    /// 是否激活
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 风险设置
    /// </summary>
    public RiskSettings? RiskSettings { get; set; }

    /// <summary>
    /// 元数据
    /// </summary>
    public StrategyMetadata? Metadata { get; set; }
}

/// <summary>
/// 风险设置
/// </summary>
public class RiskSettings
{
    /// <summary>
    /// 最大持仓比例（百分比）
    /// </summary>
    public decimal MaxPositionPercent { get; set; } = 10;

    /// <summary>
    /// 止损比例（百分比）
    /// </summary>
    public decimal StopLossPercent { get; set; } = 5;

    /// <summary>
    /// 止盈比例（百分比）
    /// </summary>
    public decimal TakeProfitPercent { get; set; } = 15;
}

/// <summary>
/// 策略元数据
/// </summary>
public class StrategyMetadata
{
    /// <summary>
    /// 作者
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// 版本
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 创建日期
    /// </summary>
    public string? CreatedDate { get; set; }

    /// <summary>
    /// 标签
    /// </summary>
    public List<string> Tags { get; set; } = new();
}