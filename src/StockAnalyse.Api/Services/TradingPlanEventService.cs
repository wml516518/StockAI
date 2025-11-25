using System.Collections.Concurrent;

namespace StockAnalyse.Api.Services;

/// <summary>
/// 做T方案事件通知服务（用于SSE推送）
/// </summary>
public class TradingPlanEventService
{
    // 存储所有连接的客户端
    private readonly ConcurrentDictionary<string, StreamWriter> _clients = new();
    private readonly ILogger<TradingPlanEventService> _logger;

    public TradingPlanEventService(ILogger<TradingPlanEventService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 添加客户端连接
    /// </summary>
    public string AddClient(StreamWriter writer)
    {
        var clientId = Guid.NewGuid().ToString();
        _clients.TryAdd(clientId, writer);
        _logger.LogDebug("客户端已连接: {ClientId}, 当前连接数: {Count}", clientId, _clients.Count);
        return clientId;
    }

    /// <summary>
    /// 移除客户端连接
    /// </summary>
    public void RemoveClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var writer))
        {
            try
            {
                writer?.Dispose();
            }
            catch { }
            _logger.LogDebug("客户端已断开: {ClientId}, 当前连接数: {Count}", clientId, _clients.Count);
        }
    }

    /// <summary>
    /// 通知所有客户端做T方案已更新
    /// </summary>
    public async Task NotifyTradingPlanUpdatedAsync(int watchlistStockId, string stockCode, DateTime updateTime)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var message = $@"data: {{
  ""type"": ""tradingPlanUpdated"",
  ""watchlistStockId"": {watchlistStockId},
  ""stockCode"": ""{stockCode}"",
  ""updateTime"": ""{updateTime:yyyy-MM-ddTHH:mm:ss.fffZ}""
}}

";

        var disconnectedClients = new List<string>();

        foreach (var (clientId, writer) in _clients)
        {
            try
            {
                await writer.WriteAsync(message);
                await writer.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "推送消息到客户端失败: {ClientId}", clientId);
                disconnectedClients.Add(clientId);
            }
        }

        // 清理断开的连接
        foreach (var clientId in disconnectedClients)
        {
            RemoveClient(clientId);
        }

        if (disconnectedClients.Count > 0)
        {
            _logger.LogInformation("已清理 {Count} 个断开的连接", disconnectedClients.Count);
        }
    }

    /// <summary>
    /// 发送心跳包（保持连接）
    /// </summary>
    public async Task SendHeartbeatAsync()
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var heartbeat = "data: {\"type\":\"heartbeat\"}\n\n";

        var disconnectedClients = new List<string>();

        foreach (var (clientId, writer) in _clients)
        {
            try
            {
                await writer.WriteAsync(heartbeat);
                await writer.FlushAsync();
            }
            catch
            {
                disconnectedClients.Add(clientId);
            }
        }

        foreach (var clientId in disconnectedClients)
        {
            RemoveClient(clientId);
        }
    }
}

