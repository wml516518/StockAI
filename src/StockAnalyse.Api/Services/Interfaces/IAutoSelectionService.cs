using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

/// <summary>
/// 自动选股服务接口
/// </summary>
public interface IAutoSelectionService
{
    /// <summary>
    /// 执行自动选股（不保存到自选股，只返回结果）
    /// </summary>
    Task<AutoSelectionResult> ExecuteSelectionAsync(CancellationToken cancellationToken = default);
}

