using Newtonsoft.Json;
using System.Collections.Generic;

namespace StockAnalyse.Api.Services.Abstractions;

public class AiChatMessage
{
    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty;

    [JsonProperty("content")]
    public string? Content { get; set; }

    [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
    public List<AiToolCall>? ToolCalls { get; set; }

    [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? ToolCallId { get; set; }

    public AiChatMessage() { }

    public AiChatMessage(string role, string? content)
    {
        Role = role;
        Content = content;
    }

    public static AiChatMessage Create(string role, string? content)
    {
        return new AiChatMessage(
            string.IsNullOrWhiteSpace(role) ? "user" : role.Trim().ToLowerInvariant(),
            content);
    }
}

public class AiToolCall
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = "function";

    [JsonProperty("function")]
    public AiFunctionCall Function { get; set; } = new();
}

public class AiFunctionCall
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("arguments")]
    public string Arguments { get; set; } = string.Empty;
}

