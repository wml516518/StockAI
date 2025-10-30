using System;

namespace StockAnalyse.Api.Models
{
    public class SimulatedTradeItem
    {
        public string StockCode { get; set; } = string.Empty;
        public TradeType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Commission { get; set; }
        public decimal Amount { get; set; }
        public DateTime ExecutedAt { get; set; }
    }

    public class StockBacktestSummary
    {
        public string StockCode { get; set; } = string.Empty;
        public decimal InitialCapital { get; set; }
        public decimal FinalCapital { get; set; }
        public decimal TotalReturn { get; set; }
        public decimal AnnualizedReturn { get; set; }
        public decimal SharpeRatio { get; set; }
        public decimal MaxDrawdown { get; set; }
        public int TotalTrades { get; set; }
        public decimal WinRate { get; set; }
        public System.Collections.Generic.List<SimulatedTradeItem> Trades { get; set; } = new();
    }
}