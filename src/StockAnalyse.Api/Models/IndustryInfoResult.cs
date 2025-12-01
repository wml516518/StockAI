using System.Collections.Generic;

namespace StockAnalyse.Api.Models;

public class IndustryInfoResult
{
    public string InfoText { get; set; } = string.Empty;
    public string? IndustryName { get; set; }
    public string? IndustryCode { get; set; }
    public List<string> Keywords { get; set; } = new();
}
