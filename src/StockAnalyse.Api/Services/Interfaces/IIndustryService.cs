using System.Threading.Tasks;
using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IIndustryService
{
    Task<IndustryInfoResult?> GetIndustryInfoFromAKShareAsync(string stockCode);
}
