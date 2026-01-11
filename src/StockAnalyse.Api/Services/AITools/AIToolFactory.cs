using Newtonsoft.Json.Linq;
using StockAnalyse.Api.Services.Abstractions;
using StockAnalyse.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace StockAnalyse.Api.Services.AITools;

public interface IAIToolFactory
{
    List<AiTool> GetAllDefinitions();
    Task<string> ExecuteToolAsync(string toolName, string argumentsJson);
}

public class AIToolFactory : IAIToolFactory
{
    private readonly IEnumerable<IAITool> _tools;
    private readonly ILogger<AIToolFactory> _logger;

    public AIToolFactory(IEnumerable<IAITool> tools, ILogger<AIToolFactory> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    public List<AiTool> GetAllDefinitions()
    {
        return _tools.Select(t => t.GetDefinition()).ToList();
    }

    public async Task<string> ExecuteToolAsync(string toolName, string argumentsJson)
    {
        var tool = _tools.FirstOrDefault(t => t.Name.ToToolName() == toolName);
        if (tool == null)
        {
            _logger.LogWarning("尝试调用未注册的工具: {ToolName}", toolName);
            return $"错误: 未知工具 {toolName}";
        }

        try
        {
            var args = JObject.Parse(argumentsJson);
            return await tool.ExecuteAsync(args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行工具 {ToolName} 失败", toolName);
            return $"错误: 执行工具 {toolName} 失败。详细信息: {ex.Message}";
        }
    }
}
