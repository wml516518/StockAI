namespace StockAnalyse.Api.Services.Abstractions;

public enum AIToolName
{
    GetStockQuote,
    GetStockNews,
    GetStockFundamentals,
    GetIndustryInfo,
    GetMarketSentiment,
    GetPriceHistory
}

public static class AIToolNameExtensions
{
    public static string ToToolName(this AIToolName name)
    {
        return name switch
        {
            AIToolName.GetStockQuote => "get_stock_quote",
            AIToolName.GetStockNews => "get_stock_news",
            AIToolName.GetStockFundamentals => "get_stock_fundamentals",
            AIToolName.GetIndustryInfo => "get_industry_info",
            AIToolName.GetMarketSentiment => "get_market_sentiment",
            AIToolName.GetPriceHistory => "get_price_history",
            _ => name.ToString().ToLowerInvariant()
        };
    }
}
