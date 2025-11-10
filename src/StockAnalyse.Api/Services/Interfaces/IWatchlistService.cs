using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces;

public interface IWatchlistService
{
    /// <summary>
    /// 添加自选股
    /// </summary>
    Task<WatchlistStock> AddToWatchlistAsync(string stockCode, int categoryId, decimal? costPrice = null, decimal? quantity = null);
    
    /// <summary>
    /// 从自选股中移除
    /// </summary>
    Task<bool> RemoveFromWatchlistAsync(int watchlistStockId);
    
    /// <summary>
    /// 更新自选股成本信息
    /// </summary>
    Task<WatchlistStock> UpdateCostInfoAsync(int id, decimal? costPrice, decimal? quantity);
    
    /// <summary>
    /// 更新自选股价格提醒
    /// </summary>
    Task<WatchlistStock> UpdatePriceAlertAsync(int id, decimal? highAlert, decimal? lowAlert);
    
    /// <summary>
    /// 获取用户所有自选股（按分类）
    /// </summary>
    Task<Dictionary<string, List<WatchlistStock>>> GetWatchlistGroupedByCategoryAsync();
    
    /// <summary>
    /// 获取指定分类的自选股
    /// </summary>
    Task<List<WatchlistStock>> GetWatchlistByCategoryAsync(int categoryId);
    
    /// <summary>
    /// 计算自选股盈亏
    /// </summary>
    Task<WatchlistStock> CalculateProfitLossAsync(int id);
    
    /// <summary>
    /// 获取自选股分类列表
    /// </summary>
    Task<List<WatchlistCategory>> GetCategoriesAsync();
    
    /// <summary>
    /// 创建分类
    /// </summary>
    Task<WatchlistCategory> CreateCategoryAsync(string name, string? description = null, string color = "#1890ff");
    
    /// <summary>
    /// 删除分类
    /// </summary>
    Task<bool> DeleteCategoryAsync(int id);
    
    /// <summary>
    /// 更新自选股分类
    /// </summary>
    Task<WatchlistStock> UpdateCategoryAsync(int id, int categoryId);
    
    /// <summary>
    /// 确保股票仅归属目标分类，返回处理结果
    /// </summary>
    Task<WatchlistMoveResult> MoveStockToCategoryAsync(string stockCode, int targetCategoryId);
    
    /// <summary>
    /// 更新自选股建议价格
    /// </summary>
    Task<WatchlistStock> UpdateSuggestedPriceAsync(int id, decimal? suggestedBuyPrice, decimal? suggestedSellPrice);
    
    /// <summary>
    /// 重置自选股提醒标志（当价格偏离建议价格时）
    /// </summary>
    Task<WatchlistStock> ResetAlertFlagsAsync(int id, decimal currentPrice);
}

public record WatchlistMoveResult(bool Found, bool AlreadyInTarget, bool MovedToTarget);

