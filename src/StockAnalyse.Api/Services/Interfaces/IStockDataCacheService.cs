using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IStockDataCacheService
{
    Task<T?> TryGetAsync<T>(string stockCode, string dataType, bool allowExpired = false, CancellationToken cancellationToken = default);
    Task CacheAsync<T>(string stockCode, string dataType, T payload, TimeSpan ttl, bool isFallback = false, string? metadata = null, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string stockCode, string dataType, CancellationToken cancellationToken = default);
    Task<StockDataCache?> GetRawAsync(string stockCode, string dataType, CancellationToken cancellationToken = default);
}


