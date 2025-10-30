using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

/// <summary>
/// 策略配置服务接口
/// </summary>
public interface IStrategyConfigService
{
    /// <summary>
    /// 加载所有默认策略配置
    /// </summary>
    /// <returns>策略配置列表</returns>
    Task<List<Models.StrategyConfig>> LoadDefaultStrategiesAsync();

    /// <summary>
    /// 根据名称加载策略配置
    /// </summary>
    /// <param name="strategyName">策略名称</param>
    /// <returns>策略配置</returns>
    Task<Models.StrategyConfig?> LoadStrategyByNameAsync(string strategyName);

    /// <summary>
    /// 保存策略配置到文件
    /// </summary>
    /// <param name="config">策略配置</param>
    /// <param name="fileName">文件名（可选）</param>
    /// <returns>保存是否成功</returns>
    Task<bool> SaveStrategyConfigAsync(Models.StrategyConfig config, string? fileName = null);

    /// <summary>
    /// 删除策略配置文件
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <returns>删除是否成功</returns>
    Task<bool> DeleteStrategyConfigAsync(string fileName);

    /// <summary>
    /// 获取所有可用的策略配置文件名
    /// </summary>
    /// <returns>文件名列表</returns>
    Task<List<string>> GetAvailableConfigFilesAsync();

    /// <summary>
    /// 将策略配置转换为量化策略实体
    /// </summary>
    /// <param name="config">策略配置</param>
    /// <returns>量化策略实体</returns>
    QuantStrategy ConvertToQuantStrategy(Models.StrategyConfig config);

    /// <summary>
    /// 批量导入策略配置到数据库
    /// </summary>
    /// <returns>导入的策略数量</returns>
    Task<int> ImportStrategiesToDatabaseAsync();
}