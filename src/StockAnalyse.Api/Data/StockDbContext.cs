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
    public DbSet<PriceAlert> PriceAlerts { get; set; }

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
    }
}

