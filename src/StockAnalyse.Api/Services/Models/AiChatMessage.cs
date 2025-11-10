namespace StockAnalyse.Api.Services.Abstractions;

public record AiChatMessage(string Role, string Content)
{
    public static AiChatMessage Create(string role, string content)
    {
        return new AiChatMessage(
            string.IsNullOrWhiteSpace(role) ? "user" : role.Trim().ToLowerInvariant(),
            content ?? string.Empty);
    }
}

