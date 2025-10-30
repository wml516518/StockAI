using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

/// <summary>
/// 技术指标服务接口
/// </summary>
public interface ITechnicalIndicatorService
{
    /// <summary>
    /// 计算简单移动平均线
    /// </summary>
    Task<List<decimal>> CalculateSMAAsync(string stockCode, int period, int count = 100);
    
    /// <summary>
    /// 计算指数移动平均线
    /// </summary>
    Task<List<decimal>> CalculateEMAAsync(string stockCode, int period, int count = 100);
    
    /// <summary>
    /// 计算MACD指标
    /// </summary>
    Task<List<(decimal macd, decimal signal, decimal histogram)>> CalculateMACDAsync(string stockCode, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9, int count = 100);
    
    /// <summary>
    /// 计算RSI指标
    /// </summary>
    Task<List<decimal>> CalculateRSIAsync(string stockCode, int period = 14, int count = 100);
    
    /// <summary>
    /// 计算布林带
    /// </summary>
    Task<List<(decimal upper, decimal middle, decimal lower)>> CalculateBollingerBandsAsync(string stockCode, int period = 20, decimal stdDev = 2, int count = 100);
    
    /// <summary>
    /// 检测金叉死叉
    /// </summary>
    Task<(bool isGoldenCross, bool isDeathCross)> DetectCrossoverAsync(string stockCode, int shortPeriod = 5, int longPeriod = 20);
    
    /// <summary>
    /// 生成MA交叉策略信号
    /// </summary>
    Task<TradingSignal?> GenerateMASignalAsync(string stockCode, TechnicalIndicatorParameters parameters);
    
    /// <summary>
    /// 生成MACD策略信号
    /// </summary>
    Task<TradingSignal?> GenerateMACDSignalAsync(string stockCode, TechnicalIndicatorParameters parameters);
    
    /// <summary>
    /// 生成RSI策略信号
    /// </summary>
    Task<TradingSignal?> GenerateRSISignalAsync(string stockCode, TechnicalIndicatorParameters parameters);
}