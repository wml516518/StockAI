using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Data;

public class StockDbContext : DbContext
{
    public StockDbContext(DbContextOptions<StockDbContext> options) : base(options)
    {
    }

    public DbSet<Stock> Stocks { get; set; }
    public DbSet<WatchlistStock> WatchlistStocks { get; set; }
    public DbSet<WatchlistCategory> WatchlistCategories { get; set; }
    public DbSet<StockHistory> StockHistories { get; set; }
    public DbSet<FinancialNews> FinancialNews { get; set; }
    public DbSet<AIModelConfig> AIModelConfigs { get; set; }
    public DbSet<AIPrompt> AIPrompts { get; set; }
    public DbSet<PriceAlert> PriceAlerts { get; set; }
    public DbSet<ScreenTemplate> ScreenTemplates { get; set; } // 新增选股模板
    
    // 量化交易相关表
    public DbSet<QuantStrategy> QuantStrategies { get; set; }
    public DbSet<TradingSignal> TradingSignals { get; set; }
    public DbSet<SimulatedTrade> SimulatedTrades { get; set; }
    public DbSet<BacktestResult> BacktestResults { get; set; }
    
    // 策略优化相关表
    public DbSet<StrategyOptimizationResult> StrategyOptimizationResults { get; set; }
    public DbSet<ParameterTestResult> ParameterTestResults { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置关系
        modelBuilder.Entity<WatchlistStock>()
            .HasOne(w => w.Stock)
            .WithMany(s => s.Watchlists)
            .HasForeignKey(w => w.StockCode)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WatchlistStock>()
            .HasOne(w => w.Category)
            .WithMany(c => c.Stocks)
            .HasForeignKey(w => w.WatchlistCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StockHistory>()
            .HasOne(h => h.Stock)
            .WithMany(s => s.Histories)
            .HasForeignKey(h => h.StockCode)
            .OnDelete(DeleteBehavior.Cascade);

        // 索引
        modelBuilder.Entity<Stock>()
            .HasIndex(s => s.Code);

        modelBuilder.Entity<StockHistory>()
            .HasIndex(h => new { h.StockCode, h.TradeDate });

        // 量化交易相关关系配置
        modelBuilder.Entity<TradingSignal>()
            .HasOne(ts => ts.Strategy)
            .WithMany(s => s.TradingSignals)
            .HasForeignKey(ts => ts.StrategyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SimulatedTrade>()
            .HasOne(st => st.Strategy)
            .WithMany(s => s.SimulatedTrades)
            .HasForeignKey(st => st.StrategyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BacktestResult>()
            .HasOne(br => br.Strategy)
            .WithMany(s => s.BacktestResults)
            .HasForeignKey(br => br.StrategyId)
            .OnDelete(DeleteBehavior.Cascade);

        // 量化交易相关索引
        modelBuilder.Entity<TradingSignal>()
            .HasIndex(ts => new { ts.StrategyId, ts.StockCode, ts.GeneratedAt });

        modelBuilder.Entity<SimulatedTrade>()
            .HasIndex(st => new { st.StrategyId, st.StockCode, st.ExecutedAt });

        modelBuilder.Entity<BacktestResult>()
            .HasIndex(br => new { br.StrategyId, br.StartDate, br.EndDate });
    }
}

