using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class StockDataCacheService : IStockDataCacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly StockDbContext _context;
    private readonly ILogger<StockDataCacheService> _logger;

    public StockDataCacheService(StockDbContext context, ILogger<StockDataCacheService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<T?> TryGetAsync<T>(string stockCode, string dataType, bool allowExpired = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stockCode) || string.IsNullOrWhiteSpace(dataType))
        {
            return default;
        }

        stockCode = NormalizeStockCode(stockCode);
        dataType = dataType.Trim().ToLowerInvariant();

        var cacheEntry = await _context.StockDataCaches
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StockCode == stockCode && c.DataType == dataType, cancellationToken);

        if (cacheEntry == null)
        {
            return default;
        }

        var utcNow = DateTime.UtcNow;
        if (!allowExpired && cacheEntry.ExpiresAtUtc.HasValue && cacheEntry.ExpiresAtUtc.Value <= utcNow)
        {
            _logger.LogDebug("缓存条目已过期，数据类型: {DataType}, 股票: {StockCode}, 过期时间: {ExpiresAt}", dataType, stockCode, cacheEntry.ExpiresAtUtc);
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(cacheEntry.Payload, SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "反序列化缓存失败，数据类型: {DataType}, 股票: {StockCode}", dataType, stockCode);
            return default;
        }
    }

    public async Task CacheAsync<T>(string stockCode, string dataType, T payload, TimeSpan ttl, bool isFallback = false, string? metadata = null, CancellationToken cancellationToken = default)
    {
        if (payload == null || string.IsNullOrWhiteSpace(stockCode) || string.IsNullOrWhiteSpace(dataType))
        {
            return;
        }

        stockCode = NormalizeStockCode(stockCode);
        dataType = dataType.Trim().ToLowerInvariant();

        var payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);
        var payloadHash = ComputeSha256(payloadJson);

        var utcNow = DateTime.UtcNow;
        var expiresAt = ttl == TimeSpan.Zero ? (DateTime?)null : utcNow.Add(ttl);

        var cacheEntry = await _context.StockDataCaches
            .FirstOrDefaultAsync(c => c.StockCode == stockCode && c.DataType == dataType, cancellationToken);

        if (cacheEntry == null)
        {
            cacheEntry = new StockDataCache
            {
                StockCode = stockCode,
                DataType = dataType,
                Payload = payloadJson,
                PayloadHash = payloadHash,
                CreatedAtUtc = utcNow,
                LastRefreshedUtc = utcNow,
                ExpiresAtUtc = expiresAt,
                Metadata = metadata,
                IsFallbackData = isFallback
            };

            _context.StockDataCaches.Add(cacheEntry);
        }
        else
        {
            cacheEntry.Payload = payloadJson;
            cacheEntry.PayloadHash = payloadHash;
            cacheEntry.LastRefreshedUtc = utcNow;
            cacheEntry.ExpiresAtUtc = expiresAt;
            cacheEntry.Metadata = metadata;
            cacheEntry.IsFallbackData = isFallback;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidateAsync(string stockCode, string dataType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stockCode) || string.IsNullOrWhiteSpace(dataType))
        {
            return;
        }

        stockCode = NormalizeStockCode(stockCode);
        dataType = dataType.Trim().ToLowerInvariant();

        var cacheEntry = await _context.StockDataCaches
            .FirstOrDefaultAsync(c => c.StockCode == stockCode && c.DataType == dataType, cancellationToken);

        if (cacheEntry != null)
        {
            _context.StockDataCaches.Remove(cacheEntry);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<StockDataCache?> GetRawAsync(string stockCode, string dataType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stockCode) || string.IsNullOrWhiteSpace(dataType))
        {
            return null;
        }

        stockCode = NormalizeStockCode(stockCode);
        dataType = dataType.Trim().ToLowerInvariant();

        return await _context.StockDataCaches
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StockCode == stockCode && c.DataType == dataType, cancellationToken);
    }

    private static string NormalizeStockCode(string stockCode)
    {
        return stockCode.Trim().ToUpperInvariant();
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }
}


