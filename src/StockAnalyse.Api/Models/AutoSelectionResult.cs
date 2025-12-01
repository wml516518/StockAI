namespace StockAnalyse.Api.Models;

/// <summary>
/// 自动选股结果
/// </summary>
public class AutoSelectionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalStocks { get; set; }
    public int FilteredCount { get; set; }
    public int ScoredCount { get; set; }
    public int SelectedCount { get; set; }
    public List<AutoSelectedStock> SelectedStocks { get; set; } = new();
    public bool SavedToWatchlist { get; set; }
    public int SavedCount { get; set; }
    public int SkippedCount { get; set; }
}

/// <summary>
/// 自动选中的股票信息
/// </summary>
public class AutoSelectedStock
{
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal TurnoverRate { get; set; }
    public decimal Volume { get; set; }
    public int AIScore { get; set; }
    public string? IndustryName { get; set; }
    public string HotRank { get; set; } = string.Empty;
}

