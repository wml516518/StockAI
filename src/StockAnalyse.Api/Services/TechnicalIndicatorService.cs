using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class TechnicalIndicatorService : ITechnicalIndicatorService
{
    private readonly StockDbContext _context;
    private readonly ILogger<TechnicalIndicatorService> _logger;

    public TechnicalIndicatorService(StockDbContext context, ILogger<TechnicalIndicatorService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<decimal>> CalculateSMAAsync(string stockCode, int period, int count = 100)
    {
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode)
            .OrderByDescending(h => h.TradeDate)
            .Take(count + period)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();

        if (histories.Count < period)
            return new List<decimal>();

        var smaValues = new List<decimal>();
        
        for (int i = period - 1; i < histories.Count; i++)
        {
            var sum = histories.Skip(i - period + 1).Take(period).Sum(h => h.Close);
            smaValues.Add(sum / period);
        }

        return smaValues.TakeLast(count).ToList();
    }

    public async Task<List<decimal>> CalculateEMAAsync(string stockCode, int period, int count = 100)
    {
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode)
            .OrderByDescending(h => h.TradeDate)
            .Take(count + period * 2)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();

        if (histories.Count < period)
            return new List<decimal>();

        var emaValues = new List<decimal>();
        var multiplier = 2.0m / (period + 1);

        // 第一个EMA值使用SMA
        var firstSMA = histories.Take(period).Average(h => h.Close);
        emaValues.Add(firstSMA);

        for (int i = period; i < histories.Count; i++)
        {
            var ema = (histories[i].Close * multiplier) + (emaValues.Last() * (1 - multiplier));
            emaValues.Add(ema);
        }

        return emaValues.TakeLast(count).ToList();
    }

    public async Task<List<(decimal macd, decimal signal, decimal histogram)>> CalculateMACDAsync(string stockCode, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9, int count = 100)
    {
        var fastEMA = await CalculateEMAAsync(stockCode, fastPeriod, count + slowPeriod);
        var slowEMA = await CalculateEMAAsync(stockCode, slowPeriod, count + slowPeriod);

        if (fastEMA.Count == 0 || slowEMA.Count == 0)
            return new List<(decimal, decimal, decimal)>();

        var macdLine = new List<decimal>();
        var minCount = Math.Min(fastEMA.Count, slowEMA.Count);
        
        for (int i = 0; i < minCount; i++)
        {
            macdLine.Add(fastEMA[i] - slowEMA[i]);
        }

        // 计算信号线（MACD的EMA）
        var signalLine = CalculateEMAFromValues(macdLine, signalPeriod);
        
        var result = new List<(decimal macd, decimal signal, decimal histogram)>();
        var startIndex = Math.Max(0, macdLine.Count - count);
        
        for (int i = startIndex; i < macdLine.Count && i < signalLine.Count; i++)
        {
            var histogram = macdLine[i] - signalLine[i];
            result.Add((macdLine[i], signalLine[i], histogram));
        }

        return result;
    }

    public async Task<List<decimal>> CalculateRSIAsync(string stockCode, int period = 14, int count = 100)
    {
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode)
            .OrderByDescending(h => h.TradeDate)
            .Take(count + period + 1)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();

        if (histories.Count < period + 1)
            return new List<decimal>();

        var rsiValues = new List<decimal>();
        var gains = new List<decimal>();
        var losses = new List<decimal>();

        // 计算价格变化
        for (int i = 1; i < histories.Count; i++)
        {
            var change = histories[i].Close - histories[i - 1].Close;
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? Math.Abs(change) : 0);
        }

        if (gains.Count < period)
            return new List<decimal>();

        // 计算第一个RSI值
        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();
        
        for (int i = period - 1; i < gains.Count; i++)
        {
            if (i > period - 1)
            {
                avgGain = (avgGain * (period - 1) + gains[i]) / period;
                avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
            }

            var rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
            var rsi = 100 - (100 / (1 + rs));
            rsiValues.Add(rsi);
        }

        return rsiValues.TakeLast(count).ToList();
    }

    public async Task<List<(decimal upper, decimal middle, decimal lower)>> CalculateBollingerBandsAsync(string stockCode, int period = 20, decimal stdDev = 2, int count = 100)
    {
        var sma = await CalculateSMAAsync(stockCode, period, count + period);
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode)
            .OrderByDescending(h => h.TradeDate)
            .Take(count + period)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();

        if (sma.Count == 0 || histories.Count < period)
            return new List<(decimal, decimal, decimal)>();

        var result = new List<(decimal upper, decimal middle, decimal lower)>();
        
        for (int i = period - 1; i < histories.Count && (i - period + 1) < sma.Count; i++)
        {
            var prices = histories.Skip(i - period + 1).Take(period).Select(h => h.Close).ToList();
            var mean = prices.Average();
            var variance = prices.Sum(p => (p - mean) * (p - mean)) / period;
            var standardDeviation = (decimal)Math.Sqrt((double)variance);
            
            var middle = sma[i - period + 1];
            var upper = middle + (stdDev * standardDeviation);
            var lower = middle - (stdDev * standardDeviation);
            
            result.Add((upper, middle, lower));
        }

        return result.TakeLast(count).ToList();
    }

    public async Task<(bool isGoldenCross, bool isDeathCross)> DetectCrossoverAsync(string stockCode, int shortPeriod = 5, int longPeriod = 20)
    {
        var shortMA = await CalculateSMAAsync(stockCode, shortPeriod, 2);
        var longMA = await CalculateSMAAsync(stockCode, longPeriod, 2);

        if (shortMA.Count < 2 || longMA.Count < 2)
            return (false, false);

        var prevShort = shortMA[shortMA.Count - 2];
        var currShort = shortMA[shortMA.Count - 1];
        var prevLong = longMA[longMA.Count - 2];
        var currLong = longMA[longMA.Count - 1];

        var isGoldenCross = prevShort <= prevLong && currShort > currLong;
        var isDeathCross = prevShort >= prevLong && currShort < currLong;

        return (isGoldenCross, isDeathCross);
    }

    public async Task<TradingSignal?> GenerateMASignalAsync(string stockCode, TechnicalIndicatorParameters parameters)
    {
        var (isGoldenCross, isDeathCross) = await DetectCrossoverAsync(stockCode, parameters.ShortPeriod, parameters.LongPeriod);
        
        if (!isGoldenCross && !isDeathCross)
            return null;

        var stock = await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stockCode);
        if (stock == null)
            return null;

        return new TradingSignal
        {
            StockCode = stockCode,
            Type = isGoldenCross ? SignalType.Buy : SignalType.Sell,
            Price = stock.CurrentPrice,
            Confidence = 0.7m,
            Reason = isGoldenCross ? $"MA{parameters.ShortPeriod}金叉MA{parameters.LongPeriod}" : $"MA{parameters.ShortPeriod}死叉MA{parameters.LongPeriod}",
            GeneratedAt = DateTime.Now
        };
    }

    public async Task<TradingSignal?> GenerateMACDSignalAsync(string stockCode, TechnicalIndicatorParameters parameters)
    {
        var macdData = await CalculateMACDAsync(stockCode, parameters.FastPeriod, parameters.SlowPeriod, parameters.SignalPeriod, 2);
        
        if (macdData.Count < 2)
            return null;

        var prev = macdData[macdData.Count - 2];
        var curr = macdData[macdData.Count - 1];

        // MACD金叉死叉判断
        var isGoldenCross = prev.macd <= prev.signal && curr.macd > curr.signal;
        var isDeathCross = prev.macd >= prev.signal && curr.macd < curr.signal;

        if (!isGoldenCross && !isDeathCross)
            return null;

        var stock = await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stockCode);
        if (stock == null)
            return null;

        return new TradingSignal
        {
            StockCode = stockCode,
            Type = isGoldenCross ? SignalType.Buy : SignalType.Sell,
            Price = stock.CurrentPrice,
            Confidence = 0.75m,
            Reason = isGoldenCross ? "MACD金叉" : "MACD死叉",
            GeneratedAt = DateTime.Now
        };
    }

    public async Task<TradingSignal?> GenerateRSISignalAsync(string stockCode, TechnicalIndicatorParameters parameters)
    {
        var rsiValues = await CalculateRSIAsync(stockCode, parameters.RSIPeriod, 1);
        
        if (rsiValues.Count == 0)
            return null;

        var currentRSI = rsiValues.Last();
        SignalType? signalType = null;
        string? reason = null;

        if (currentRSI <= parameters.RSIOverSold)
        {
            signalType = SignalType.Buy;
            reason = $"RSI超卖({currentRSI:F2})";
        }
        else if (currentRSI >= parameters.RSIOverBought)
        {
            signalType = SignalType.Sell;
            reason = $"RSI超买({currentRSI:F2})";
        }

        if (signalType == null)
            return null;

        var stock = await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stockCode);
        if (stock == null)
            return null;

        return new TradingSignal
        {
            StockCode = stockCode,
            Type = signalType.Value,
            Price = stock.CurrentPrice,
            Confidence = 0.6m,
            Reason = reason,
            GeneratedAt = DateTime.Now
        };
    }

    private List<decimal> CalculateEMAFromValues(List<decimal> values, int period)
    {
        if (values.Count < period)
            return new List<decimal>();

        var emaValues = new List<decimal>();
        var multiplier = 2.0m / (period + 1);

        // 第一个EMA值使用SMA
        var firstSMA = values.Take(period).Average();
        emaValues.Add(firstSMA);

        for (int i = period; i < values.Count; i++)
        {
            var ema = (values[i] * multiplier) + (emaValues.Last() * (1 - multiplier));
            emaValues.Add(ema);
        }

        return emaValues;
    }
}