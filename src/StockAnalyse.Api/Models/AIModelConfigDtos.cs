namespace StockAnalyse.Api.Models;

public sealed class AIModelConfigRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string SubscribeEndpoint { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
}

public sealed record AIModelConfigResponse(
    int Id,
    string Name,
    string SubscribeEndpoint,
    string? ModelName,
    bool IsActive,
    bool IsDefault,
    bool HasApiKey);

public sealed class TestConnectionRequest
{
    public int Id { get; set; }
    public string? ApiKey { get; set; }
    public string? SubscribeEndpoint { get; set; }
    public string? ModelName { get; set; }
}
