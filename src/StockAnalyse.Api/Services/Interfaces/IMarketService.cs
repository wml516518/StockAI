using System.Threading.Tasks;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IMarketService
{
    Task<string> GetHotRankFromAKShareAsync(string stockCode);
}
