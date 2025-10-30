using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;

namespace StockAnalyse.Api.Services;

public class QuantTradingService : IQuantTradingService
{
    private readonly StockDbContext _context;
    private readonly ITechnicalIndicatorService _technicalIndicatorService;
    private readonly ILogger<QuantTradingService> _logger;

    public QuantTradingService(
        StockDbContext context,
        ITechnicalIndicatorService technicalIndicatorService,
        ILogger<QuantTradingService> logger)
    {
        _context = context;
        _technicalIndicatorService = technicalIndicatorService;
        _logger = logger;
    }

    public async Task<List<QuantStrategy>> GetAllStrategiesAsync()
    {
        return await _context.QuantStrategies
            .Include(s => s.TradingSignals)
            .Include(s => s.SimulatedTrades)
            .Include(s => s.BacktestResults)
            .ToListAsync();
    }

    public async Task<QuantStrategy?> GetStrategyByIdAsync(int id)
    {
        return await _context.QuantStrategies
            .Include(s => s.TradingSignals)
            .Include(s => s.SimulatedTrades)
            .Include(s => s.BacktestResults)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<QuantStrategy> CreateStrategyAsync(QuantStrategy strategy)
    {
        _context.QuantStrategies.Add(strategy);
        await _context.SaveChangesAsync();
        return strategy;
    }

    public async Task<QuantStrategy> UpdateStrategyAsync(QuantStrategy strategy)
    {
        _context.QuantStrategies.Update(strategy);
        await _context.SaveChangesAsync();
        return strategy;
    }

    public async Task<bool> DeleteStrategyAsync(int id)
    {
        var strategy = await _context.QuantStrategies.FindAsync(id);
        if (strategy == null)
            return false;

        _context.QuantStrategies.Remove(strategy);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<TradingSignal>> RunStrategyAsync(int strategyId, List<string>? stockCodes = null)
    {
        var strategy = await GetStrategyByIdAsync(strategyId);
        if (strategy == null || !strategy.IsActive)
            return new List<TradingSignal>();

        // 如果没有指定股票代码，使用自选股
        if (stockCodes == null || stockCodes.Count == 0)
        {
            stockCodes = await _context.WatchlistStocks
                .Select(w => w.StockCode)
                .Distinct()
                .ToListAsync();
        }

        var signals = new List<TradingSignal>();
        var parameters = JsonSerializer.Deserialize<TechnicalIndicatorParameters>(strategy.Parameters) 
                        ?? new TechnicalIndicatorParameters();

        foreach (var stockCode in stockCodes)
        {
            try
            {
                TradingSignal? signal = strategy.Type switch
                {
                    StrategyType.TechnicalIndicator => await GenerateTechnicalSignalAsync(stockCode, parameters, strategy.Name),
                    _ => null
                };

                if (signal != null)
                {
                    signal.StrategyId = strategyId;
                    signals.Add(signal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成股票 {StockCode} 的交易信号时出错", stockCode);
            }
        }

        // 保存信号到数据库
        if (signals.Any())
        {
            _context.TradingSignals.AddRange(signals);
            await _context.SaveChangesAsync();

            // 更新策略最后运行时间
            strategy.LastRunAt = DateTime.Now;
            await _context.SaveChangesAsync();
        }

        return signals;
    }

    public async Task<SimulatedTrade?> ExecuteSignalAsync(int signalId)
    {
        var signal = await _context.TradingSignals
            .Include(s => s.Strategy)
            .FirstOrDefaultAsync(s => s.Id == signalId);

        if (signal == null || signal.IsExecuted)
            return null;

        var strategy = signal.Strategy;
        var quantity = CalculateTradeQuantity(strategy.CurrentCapital, signal.Price);

        if (quantity <= 0)
            return null;

        var commission = CalculateCommission(signal.Price * quantity);
        var totalAmount = (signal.Price * quantity) + commission;

        // 检查资金是否足够
        if (signal.Type == SignalType.Buy && strategy.CurrentCapital < totalAmount)
            return null;

        var trade = new SimulatedTrade
        {
            StrategyId = signal.StrategyId,
            StockCode = signal.StockCode,
            Type = signal.Type == SignalType.Buy ? TradeType.Buy : TradeType.Sell,
            Quantity = quantity,
            Price = signal.Price,
            Commission = commission,
            Amount = signal.Price * quantity,
            ExecutedAt = DateTime.Now,
            Notes = $"执行信号: {signal.Reason}"
        };

        _context.SimulatedTrades.Add(trade);

        // 更新策略资金
        if (signal.Type == SignalType.Buy)
        {
            strategy.CurrentCapital -= totalAmount;
        }
        else
        {
            strategy.CurrentCapital += (signal.Price * quantity) - commission;
        }

        // 标记信号已执行
        signal.IsExecuted = true;
        signal.ExecutedAt = DateTime.Now;

        await _context.SaveChangesAsync();
        return trade;
    }

    public async Task<List<TradingSignal>> GetStrategySignalsAsync(int strategyId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.TradingSignals.Where(s => s.StrategyId == strategyId);

        if (startDate.HasValue)
            query = query.Where(s => s.GeneratedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(s => s.GeneratedAt <= endDate.Value);

        return await query.OrderByDescending(s => s.GeneratedAt).ToListAsync();
    }

    public async Task<List<SimulatedTrade>> GetStrategyTradesAsync(int strategyId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.SimulatedTrades.Where(t => t.StrategyId == strategyId);

        if (startDate.HasValue)
            query = query.Where(t => t.ExecutedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(t => t.ExecutedAt <= endDate.Value);

        return await query.OrderByDescending(t => t.ExecutedAt).ToListAsync();
    }

    private async Task<TradingSignal?> GenerateTechnicalSignalAsync(string stockCode, TechnicalIndicatorParameters parameters, string strategyName)
    {
        // 根据策略名称判断使用哪种技术指标
        return strategyName.ToLower() switch
        {
            var name when name.Contains("ma") || name.Contains("移动平均") => 
                await _technicalIndicatorService.GenerateMASignalAsync(stockCode, parameters),
            var name when name.Contains("macd") => 
                await _technicalIndicatorService.GenerateMACDSignalAsync(stockCode, parameters),
            var name when name.Contains("rsi") => 
                await _technicalIndicatorService.GenerateRSISignalAsync(stockCode, parameters),
            _ => await _technicalIndicatorService.GenerateMASignalAsync(stockCode, parameters) // 默认使用MA策略
        };
    }

    private decimal CalculateTradeQuantity(decimal availableCapital, decimal price)
    {
        // 简单的资金管理：使用10%的资金进行单次交易
        var tradeAmount = availableCapital * 0.1m;
        var quantity = Math.Floor(tradeAmount / price / 100) * 100; // 按手数计算（100股为一手）
        return Math.Max(0, quantity);
    }

    private decimal CalculateCommission(decimal amount)
    {
        // 简单的手续费计算：万分之三，最低5元
        var commission = amount * 0.0003m;
        return Math.Max(5, commission);
    }
}