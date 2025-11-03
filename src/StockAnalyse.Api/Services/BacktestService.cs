using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;

namespace StockAnalyse.Api.Services;

public class BacktestService : IBacktestService
{
    private readonly StockDbContext _context;
    private readonly IQuantTradingService _quantTradingService;
    private readonly ITechnicalIndicatorService _technicalIndicatorService;
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<BacktestService> _logger;

    public BacktestService(
        StockDbContext context,
        IQuantTradingService quantTradingService,
        ITechnicalIndicatorService technicalIndicatorService,
        IStockDataService stockDataService,
        ILogger<BacktestService> logger)
    {
        _context = context;
        _quantTradingService = quantTradingService;
        _technicalIndicatorService = technicalIndicatorService;
        _stockDataService = stockDataService;
        _logger = logger;
    }

    // 原方法：改为回退到自选股并委托到新重载
    public async Task<BacktestResult> RunBacktestAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital = 100000)
    {
        var watchlistCodes = await _context.WatchlistStocks
            .Select(w => w.StockCode)
            .Distinct()
            .ToListAsync();

        return await RunBacktestAsync(strategyId, startDate, endDate, initialCapital, watchlistCodes);
    }

    // 新增：批量股票回测（使用传入股票集合）
    public async Task<BacktestResult> RunBacktestAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital, List<string> stockCodes)
    {
        var strategy = await _context.QuantStrategies.FindAsync(strategyId);
        if (strategy == null)
            throw new ArgumentException("策略不存在", nameof(strategyId));

        _logger.LogInformation("开始回测策略 {StrategyName}，时间范围：{StartDate} - {EndDate}", strategy.Name, startDate, endDate);

        stockCodes = stockCodes?.Distinct().ToList() ?? new List<string>();
        if (!stockCodes.Any())
        {
            _logger.LogWarning("没有找到传入的股票集合，回测无法进行");
            throw new InvalidOperationException("没有找到可用的股票进行回测");
        }

        var parameters = JsonSerializer.Deserialize<TechnicalIndicatorParameters>(strategy.Parameters) 
                        ?? new TechnicalIndicatorParameters();
        var lookbackDays = Math.Max(parameters.LongPeriod, parameters.SlowPeriod) + 50;
        var fetchStart = startDate.AddDays(-lookbackDays);

        foreach (var code in stockCodes)
        {
            try
            {
                await _stockDataService.FetchAndStoreDailyHistoryAsync(code, fetchStart, endDate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "拉取 {Code} 历史数据失败，可能影响回测信号生成", code);
            }
        }

        var trades = new List<SimulatedTrade>();
        var currentCapital = initialCapital;
        var positions = new Dictionary<string, decimal>();
        var paras = JsonSerializer.Deserialize<TechnicalIndicatorParameters>(strategy.Parameters)
                        ?? new TechnicalIndicatorParameters();

        var currentDate = startDate;
        while (currentDate <= endDate)
        {
            try
            {
                foreach (var stockCode in stockCodes)
                {
                    var signal = await GenerateHistoricalSignalAsync(stockCode, currentDate, paras, strategy.Type, strategy.Name);
                    if (signal != null)
                    {
                        var (trade, newCapital) = await ExecuteBacktestTradeAsync(signal, currentCapital, positions, strategyId);
                        if (trade != null)
                        {
                            trade.ExecutedAt = currentDate;
                            trades.Add(trade);
                            currentCapital = newCapital;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "回测日期 {Date} 处理失败", currentDate);
            }

            currentDate = currentDate.AddDays(1);
            if (currentDate.DayOfWeek == DayOfWeek.Saturday)
                currentDate = currentDate.AddDays(2);
        }

        var finalCapital = currentCapital;
        foreach (var position in positions)
        {
            if (position.Value > 0)
            {
                var latestPrice = await GetLatestPriceAsync(position.Key, endDate);
                finalCapital += position.Value * latestPrice;
            }
        }

        var (totalReturn, annualizedReturn, sharpeRatio, maxDrawdown, winRate) = 
            await CalculatePerformanceMetricsAsync(trades, initialCapital, startDate, endDate);

        var backtest = new BacktestResult
        {
            StrategyId = strategy.Id,
            StartDate = startDate,
            EndDate = endDate,
            InitialCapital = initialCapital,
            FinalCapital = finalCapital,
            TotalReturn = totalReturn,
            AnnualizedReturn = annualizedReturn,
            SharpeRatio = sharpeRatio,
            MaxDrawdown = maxDrawdown,
            TotalTrades = trades.Count,
            WinningTrades = trades.Count(t => IsWinningTrade(t, trades)),
            WinRate = winRate,
            DetailedResults = JsonSerializer.Serialize(new
            {
                Trades = trades.Select(t => new
                {
                    t.StockCode,
                    t.Type,
                    t.Quantity,
                    t.Price,
                    t.Amount,
                    t.ExecutedAt
                }),
                DailyCapital = CalculateDailyCapital(trades, initialCapital),
                FinalPositions = positions
            })
        };

        _context.BacktestResults.Add(backtest);
        await _context.SaveChangesAsync();

        _logger.LogInformation("回测完成，总收益率：{TotalReturn:P2}，交易次数：{TradeCount}", totalReturn, trades.Count);

        return backtest;
    }

    public async Task<List<BacktestResult>> GetBacktestResultsAsync(int strategyId)
    {
        return await _context.BacktestResults
            .Where(br => br.StrategyId == strategyId)
            .OrderByDescending(br => br.CreatedAt)
            .ToListAsync();
    }

    public async Task<BacktestResult?> GetBacktestResultByIdAsync(int id)
    {
        return await _context.BacktestResults
            .Include(br => br.Strategy)
            .FirstOrDefaultAsync(br => br.Id == id);
    }

    public async Task<bool> DeleteBacktestResultAsync(int id)
    {
        var result = await _context.BacktestResults.FindAsync(id);
        if (result == null)
            return false;

        _context.BacktestResults.Remove(result);
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<(decimal totalReturn, decimal annualizedReturn, decimal sharpeRatio, decimal maxDrawdown, decimal winRate)> CalculatePerformanceMetricsAsync(
        List<SimulatedTrade> trades, decimal initialCapital, DateTime startDate, DateTime endDate)
    {
        if (!trades.Any())
            return Task.FromResult((0m, 0m, 0m, 0m, 0m));

        // 计算每日资产价值
        var dailyCapital = CalculateDailyCapital(trades, initialCapital);
        
        // 总收益率
        var finalCapital = dailyCapital.LastOrDefault().Value;
        var totalReturn = (finalCapital - initialCapital) / initialCapital;

        // 年化收益率
        var days = (endDate - startDate).TotalDays;
        var years = days / 365.25;
        var annualizedReturn = years > 0 ? (decimal)(Math.Pow((double)(finalCapital / initialCapital), 1.0 / years) - 1) : 0;

        // 最大回撤
        var maxDrawdown = CalculateMaxDrawdown(dailyCapital);

        // 胜率
        var winningTrades = 0;
        var buyTrades = trades.Where(t => t.Type == TradeType.Buy).ToList();
        var sellTrades = trades.Where(t => t.Type == TradeType.Sell).ToList();

        foreach (var buyTrade in buyTrades)
        {
            var correspondingSell = sellTrades
                .Where(s => s.StockCode == buyTrade.StockCode && s.ExecutedAt > buyTrade.ExecutedAt)
                .OrderBy(s => s.ExecutedAt)
                .FirstOrDefault();

            if (correspondingSell != null && correspondingSell.Price > buyTrade.Price)
            {
                winningTrades++;
            }
        }

        var winRate = buyTrades.Any() ? (decimal)winningTrades / buyTrades.Count : 0;

        // 夏普比率（简化计算，假设无风险利率为3%）
        var riskFreeRate = 0.03m;
        var dailyReturns = CalculateDailyReturns(dailyCapital);
        var avgDailyReturn = dailyReturns.Any() ? dailyReturns.Average() : 0;
        var dailyStdDev = CalculateStandardDeviation(dailyReturns);
        var sharpeRatio = dailyStdDev > 0 ? (avgDailyReturn - riskFreeRate / 365) / dailyStdDev * (decimal)Math.Sqrt(365) : 0;

        return Task.FromResult((totalReturn, annualizedReturn, sharpeRatio, maxDrawdown, winRate));
    }

    private async Task<TradingSignal?> GenerateHistoricalSignalAsync(string stockCode, DateTime date, 
        TechnicalIndicatorParameters parameters, StrategyType strategyType, string strategyName)
    {
        // 获取历史数据到指定日期
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode && h.TradeDate <= date)
            .OrderByDescending(h => h.TradeDate)
            .Take(100)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();

        if (histories.Count < Math.Max(parameters.LongPeriod, parameters.SlowPeriod))
            return null;

        // 根据策略类型生成信号（这里需要基于历史数据重新计算指标）
        return strategyName.ToLower() switch
        {
            var name when name.Contains("ma") || name.Contains("移动平均") => 
                await GenerateHistoricalMASignalAsync(stockCode, date, parameters),
            var name when name.Contains("macd") => 
                await GenerateHistoricalMACDSignalAsync(stockCode, date, parameters),
            var name when name.Contains("rsi") => 
                await GenerateHistoricalRSISignalAsync(stockCode, date, parameters),
            _ => null
        };
    }

    private async Task<TradingSignal?> GenerateHistoricalMASignalAsync(string stockCode, DateTime date, TechnicalIndicatorParameters parameters)
    {
        // 获取足够的历史数据
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode && h.TradeDate <= date)
            .OrderByDescending(h => h.TradeDate)
            .Take(parameters.LongPeriod + 5)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();

        if (histories.Count < parameters.LongPeriod + 1)
            return null;

        // 计算最近两天的移动平均线
        var shortMA1 = histories.TakeLast(parameters.ShortPeriod + 1).Take(parameters.ShortPeriod).Average(h => h.Close);
            var shortMA2 = histories.TakeLast(parameters.ShortPeriod).Average(h => h.Close);
            var longMA1 = histories.TakeLast(parameters.LongPeriod + 1).Take(parameters.LongPeriod).Average(h => h.Close);
            var longMA2 = histories.TakeLast(parameters.LongPeriod).Average(h => h.Close);

        // 检测金叉死叉
        var isGoldenCross = shortMA1 <= longMA1 && shortMA2 > longMA2;
        var isDeathCross = shortMA1 >= longMA1 && shortMA2 < longMA2;

        if (!isGoldenCross && !isDeathCross)
            return null;

        var currentPrice = histories.Last().Close;

        return new TradingSignal
        {
            StockCode = stockCode,
            Type = isGoldenCross ? SignalType.Buy : SignalType.Sell,
            Price = currentPrice,
            Confidence = 0.7m,
            Reason = isGoldenCross ? $"MA{parameters.ShortPeriod}金叉MA{parameters.LongPeriod}" : $"MA{parameters.ShortPeriod}死叉MA{parameters.LongPeriod}",
            GeneratedAt = date
        };
    }

    private async Task<TradingSignal?> GenerateHistoricalMACDSignalAsync(string stockCode, DateTime date, TechnicalIndicatorParameters parameters)
    {
        // 获取到指定日期的历史数据（按日期升序）
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode && h.TradeDate <= date)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();

        if (histories.Count < Math.Max(parameters.SlowPeriod, parameters.SignalPeriod) + 2)
            return null;

        var closes = histories.Select(h => h.Close).ToList();
        var fast = Math.Max(2, parameters.FastPeriod);
        var slow = Math.Max(2, parameters.SlowPeriod);
        var sig = Math.Max(2, parameters.SignalPeriod);

        // 计算全序列 EMA（初始化为首值，标准做法可接受）
        List<decimal> ComputeEmaFull(List<decimal> prices, int period)
        {
            var result = new List<decimal>(prices.Count);
            var k = 2m / (period + 1);
            decimal emaPrev = prices[0];
            result.Add(emaPrev);
            for (int i = 1; i < prices.Count; i++)
            {
                emaPrev = (prices[i] - emaPrev) * k + emaPrev;
                result.Add(emaPrev);
            }
            return result;
        }

        var emaFast = ComputeEmaFull(closes, fast);
        var emaSlow = ComputeEmaFull(closes, slow);

        // MACD 线与 Signal 线
        var macdLine = new List<decimal>(closes.Count);
        for (int i = 0; i < closes.Count; i++)
        {
            macdLine.Add(emaFast[i] - emaSlow[i]);
        }
        var signalLine = ComputeEmaFull(macdLine, sig);

        // 直方图
        var histogram = new List<decimal>(closes.Count);
        for (int i = 0; i < closes.Count; i++)
        {
            histogram.Add(macdLine[i] - signalLine[i]);
        }

        // 使用最近两天的直方图变号判定金叉/死叉
        var last = histogram.Count - 1;
        if (last < 1)
            return null;

        var prevHist = histogram[last - 1];
        var currHist = histogram[last];

        var isGoldenCross = prevHist <= 0 && currHist > 0;
        var isDeathCross = prevHist >= 0 && currHist < 0;

        if (!isGoldenCross && !isDeathCross)
            return null;

        var currentPrice = histories.Last().Close;

        return new TradingSignal
        {
            StockCode = stockCode,
            Type = isGoldenCross ? SignalType.Buy : SignalType.Sell,
            Price = currentPrice,
            Confidence = 0.65m,
            Reason = isGoldenCross ? "MACD金叉（直方图由负转正）" : "MACD死叉（直方图由正转负）",
            GeneratedAt = date
        };
    }

    private async Task<TradingSignal?> GenerateHistoricalRSISignalAsync(string stockCode, DateTime date, TechnicalIndicatorParameters parameters)
    {
        // 获取到指定日期的历史数据，确保足够长度
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode && h.TradeDate <= date)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();

        if (histories.Count < parameters.RSIPeriod + 1)
            return null;

        var closes = histories.Select(h => h.Close).ToList();
        var period = Math.Max(2, parameters.RSIPeriod);

        // 初始化 Wilder 平滑平均
        decimal sumGain = 0, sumLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0) sumGain += change;
            else sumLoss += -change;
        }
        decimal avgGain = sumGain / period;
        decimal avgLoss = sumLoss / period;

        // 持续平滑到最后一个数据点
        for (int i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0;
            var loss = change < 0 ? -change : 0;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        // 计算最终 RSI
        decimal rsi;
        if (avgLoss == 0)
        {
            rsi = 100;
        }
        else
        {
            var rs = avgGain / avgLoss;
            rsi = 100 - (100 / (1 + rs));
        }

        // 阈值判定生成信号
        SignalType? signalType = null;
        string? reason = null;

        if (rsi <= parameters.RSIOverSold)
        {
            signalType = SignalType.Buy;
            reason = $"RSI超卖({rsi:F2}) ≤ {parameters.RSIOverSold}";
        }
        else if (rsi >= parameters.RSIOverBought)
        {
            signalType = SignalType.Sell;
            reason = $"RSI超买({rsi:F2}) ≥ {parameters.RSIOverBought}";
        }

        if (signalType == null)
            return null;

        var currentPrice = histories.Last().Close;

        return new TradingSignal
        {
            StockCode = stockCode,
            Type = signalType.Value,
            Price = currentPrice,
            Confidence = 0.6m,
            Reason = reason,
            GeneratedAt = date
        };
    }

    private Task<(SimulatedTrade? trade, decimal newCapital)> ExecuteBacktestTradeAsync(TradingSignal signal, decimal currentCapital, 
        Dictionary<string, decimal> positions, int strategyId)
    {
        var quantity = CalculateTradeQuantity(currentCapital, signal.Price);
        
        if (signal.Type == SignalType.Buy)
        {
            var totalCost = quantity * signal.Price * 1.0003m; // 包含手续费
            if (currentCapital < totalCost || quantity <= 0)
                return Task.FromResult<(SimulatedTrade?, decimal)>((null, currentCapital));

            var newCapital = currentCapital - totalCost;
            positions[signal.StockCode] = positions.GetValueOrDefault(signal.StockCode, 0) + quantity;

            var trade = new SimulatedTrade
            {
                StrategyId = strategyId,
                StockCode = signal.StockCode,
                Type = TradeType.Buy,
                Quantity = quantity,
                Price = signal.Price,
                Commission = quantity * signal.Price * 0.0003m,
                Amount = quantity * signal.Price
            };

            return Task.FromResult<(SimulatedTrade?, decimal)>((trade, newCapital));
        }
        else if (signal.Type == SignalType.Sell)
        {
            var availableQuantity = positions.GetValueOrDefault(signal.StockCode, 0);
            if (availableQuantity <= 0)
                return Task.FromResult<(SimulatedTrade?, decimal)>((null, currentCapital));

            var sellQuantity = Math.Min(quantity, availableQuantity);
            var totalReceived = sellQuantity * signal.Price * 0.9997m; // 扣除手续费

            var newCapital = currentCapital + totalReceived;
            positions[signal.StockCode] = availableQuantity - sellQuantity;

            var trade = new SimulatedTrade
            {
                StrategyId = strategyId,
                StockCode = signal.StockCode,
                Type = TradeType.Sell,
                Quantity = sellQuantity,
                Price = signal.Price,
                Commission = sellQuantity * signal.Price * 0.0003m,
                Amount = sellQuantity * signal.Price
            };

            return Task.FromResult<(SimulatedTrade?, decimal)>((trade, newCapital));
        }
        
        return Task.FromResult<(SimulatedTrade?, decimal)>((null, currentCapital));
    }

    private async Task<decimal> GetLatestPriceAsync(string stockCode, DateTime beforeDate)
    {
        var latestHistory = await _context.StockHistories
            .Where(h => h.StockCode == stockCode && h.TradeDate <= beforeDate)
            .OrderByDescending(h => h.TradeDate)
            .FirstOrDefaultAsync();

        return latestHistory?.Close ?? 0;
    }

    private decimal CalculateTradeQuantity(decimal availableCapital, decimal price)
    {
        var tradeAmount = availableCapital * 0.1m; // 使用10%资金
        var quantity = Math.Floor(tradeAmount / price / 100) * 100; // 按手数计算
        return Math.Max(0, quantity);
    }

    private bool IsWinningTrade(SimulatedTrade trade, List<SimulatedTrade> allTrades)
    {
        if (trade.Type == TradeType.Sell)
        {
            var correspondingBuy = allTrades
                .Where(t => t.StockCode == trade.StockCode && t.Type == TradeType.Buy && t.ExecutedAt < trade.ExecutedAt)
                .OrderByDescending(t => t.ExecutedAt)
                .FirstOrDefault();

            return correspondingBuy != null && trade.Price > correspondingBuy.Price;
        }
        return false;
    }

    private List<(DateTime Date, decimal Value)> CalculateDailyCapital(List<SimulatedTrade> trades, decimal initialCapital)
    {
        var dailyCapital = new List<(DateTime Date, decimal Value)>();
        var currentCapital = initialCapital;

        if (!trades.Any())
        {
            dailyCapital.Add((DateTime.Today, initialCapital));
            return dailyCapital;
        }

        var tradesByDate = trades.GroupBy(t => t.ExecutedAt.Date).OrderBy(g => g.Key);

        foreach (var dayTrades in tradesByDate)
        {
            foreach (var trade in dayTrades.OrderBy(t => t.ExecutedAt))
            {
                if (trade.Type == TradeType.Buy)
                {
                    currentCapital -= trade.Amount + trade.Commission;
                }
                else
                {
                    currentCapital += trade.Amount - trade.Commission;
                }
            }
            dailyCapital.Add((dayTrades.Key, currentCapital));
        }

        return dailyCapital;
    }

    private decimal CalculateMaxDrawdown(List<(DateTime Date, decimal Value)> dailyCapital)
    {
        if (!dailyCapital.Any())
            return 0;

        decimal maxDrawdown = 0;
        decimal peak = dailyCapital.First().Value;

        foreach (var (date, value) in dailyCapital)
        {
            if (value > peak)
                peak = value;

            var drawdown = (peak - value) / peak;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        return maxDrawdown;
    }

    private List<decimal> CalculateDailyReturns(List<(DateTime Date, decimal Value)> dailyCapital)
    {
        var returns = new List<decimal>();
        
        for (int i = 1; i < dailyCapital.Count; i++)
        {
            var prevValue = dailyCapital[i - 1].Value;
            var currValue = dailyCapital[i].Value;
            
            if (prevValue > 0)
            {
                returns.Add((currValue - prevValue) / prevValue);
            }
        }

        return returns;
    }

    private decimal CalculateStandardDeviation(List<decimal> values)
    {
        if (!values.Any())
            return 0;

        var mean = values.Average();
        var sumOfSquares = values.Sum(v => (v - mean) * (v - mean));
        var variance = sumOfSquares / values.Count;
        
        return (decimal)Math.Sqrt((double)variance);
    }

    public async Task<List<StockBacktestSummary>> RunBatchBacktestAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital, List<string> stockCodes)
    {
        var strategy = await _context.QuantStrategies.FindAsync(strategyId);
        if (strategy == null)
            throw new ArgumentException("策略不存在", nameof(strategyId));

        var parameters = JsonSerializer.Deserialize<TechnicalIndicatorParameters>(strategy.Parameters)
                        ?? new TechnicalIndicatorParameters();

        var lookbackDays = Math.Max(parameters.LongPeriod, parameters.SlowPeriod) + 50;
        var fetchStart = startDate.AddDays(-lookbackDays);
        stockCodes = stockCodes?.Distinct().ToList() ?? new List<string>();

        if (!stockCodes.Any())
            throw new InvalidOperationException("没有找到可用的股票进行回测");

        foreach (var code in stockCodes)
        {
            try
            {
                await _stockDataService.FetchAndStoreDailyHistoryAsync(code, fetchStart, endDate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "拉取 {Code} 历史数据失败，可能影响回测信号生成", code);
            }
        }

        var results = new List<StockBacktestSummary>();

        foreach (var stockCode in stockCodes)
        {
            var trades = new List<SimulatedTrade>();
            var positions = new Dictionary<string, decimal>();
            var currentCapital = initialCapital;

            var date = startDate;
            while (date <= endDate)
            {
                try
                {
                    var signal = await GenerateHistoricalSignalAsync(stockCode, date, parameters, strategy.Type, strategy.Name);
                    if (signal != null)
                    {
                        var (trade, newCapital) = await ExecuteBacktestTradeAsync(signal, currentCapital, positions, strategyId);
                        if (trade != null)
                        {
                            trade.ExecutedAt = date;
                            trades.Add(trade);
                            currentCapital = newCapital;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "回测股票 {Code} 在 {Date} 处理失败", stockCode, date);
                }

                date = date.AddDays(1);
                if (date.DayOfWeek == DayOfWeek.Saturday)
                    date = date.AddDays(2);
            }

            var finalCapital = currentCapital;
            var qty = positions.GetValueOrDefault(stockCode, 0);
            if (qty > 0)
            {
                var latestPrice = await GetLatestPriceAsync(stockCode, endDate);
                finalCapital += qty * latestPrice;
            }

            var (totalReturn, annualizedReturn, sharpeRatio, maxDrawdown, winRate) =
                await CalculatePerformanceMetricsAsync(trades, initialCapital, startDate, endDate);

            var summary = new StockBacktestSummary
            {
                StockCode = stockCode,
                InitialCapital = initialCapital,
                FinalCapital = finalCapital,
                TotalReturn = totalReturn,
                AnnualizedReturn = annualizedReturn,
                SharpeRatio = sharpeRatio,
                MaxDrawdown = maxDrawdown,
                TotalTrades = trades.Count,
                WinRate = winRate,
                Trades = trades.Select(t => new SimulatedTradeItem
                {
                    StockCode = t.StockCode,
                    Type = t.Type,
                    Quantity = t.Quantity,
                    Price = t.Price,
                    Commission = t.Commission,
                    Amount = t.Amount,
                    ExecutedAt = t.ExecutedAt
                }).ToList()
            };

            results.Add(summary);
        }

        return results;
    }
}