using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Data;

public static class DatabaseSeeder
{
    public static void Seed(StockDbContext context)
    {
        // 检查是否已有数据
        if (context.WatchlistCategories.Any())
        {
            return;
        }
        
        // 创建默认分类
        var categories = new List<WatchlistCategory>
        {
            new WatchlistCategory
            {
                Name = "已购",
                Description = "已经购买的股票",
                Color = "#4caf50",
                SortOrder = 1
            },
            new WatchlistCategory
            {
                Name = "预购",
                Description = "准备购买的股票",
                Color = "#ff9800",
                SortOrder = 2
            },
            new WatchlistCategory
            {
                Name = "关注",
                Description = "重点关注的目标",
                Color = "#2196f3",
                SortOrder = 3
            }
        };
        
        context.WatchlistCategories.AddRange(categories);
        
        // 添加默认AI模型配置示例
        var aiConfig = new AIModelConfig
        {
            Name = "DeepSeek",
            ApiKey = "your-api-key-here",
            SubscribeEndpoint = "https://api.deepseek.com/v1/chat/completions",
            ModelName = "deepseek-chat",
            IsActive = false,
            IsDefault = false
        };
        
        context.AIModelConfigs.Add(aiConfig);
        context.SaveChanges();
    }
}

