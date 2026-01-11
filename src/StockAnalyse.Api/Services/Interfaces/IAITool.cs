using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IAITool
{
    AIToolName Name { get; }
    AiTool GetDefinition();
    Task<string> ExecuteAsync(JObject args);
}
