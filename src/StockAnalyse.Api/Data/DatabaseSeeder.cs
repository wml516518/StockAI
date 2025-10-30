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
        
        // 添加默认选股模板
        var screenTemplates = new List<ScreenTemplate>
        {
            new ScreenTemplate
            {
                Name = "低价成长股",
                Description = "价格较低、PE合理的成长型股票",
                MinPrice = 5,
                MaxPrice = 30,
                MinPE = 10,
                MaxPE = 25,
                MinChangePercent = -5,
                MaxChangePercent = 10,
                IsDefault = true
            },
            new ScreenTemplate
            {
                Name = "高股息蓝筹",
                Description = "股息率较高的蓝筹股票",
                MinPrice = 10,
                MinDividendYield = 3,
                MaxPE = 20,
                MinMarketValue = 1000000, // 100亿以上
                IsDefault = false
            },
            new ScreenTemplate
            {
                Name = "活跃小盘股",
                Description = "换手率高、市值较小的活跃股票",
                MaxPrice = 50,
                MinTurnoverRate = 5,
                MaxMarketValue = 500000, // 50亿以下
                MinChangePercent = 0,
                IsDefault = false
            },
            new ScreenTemplate
            {
                Name = "超跌反弹",
                Description = "近期跌幅较大，可能反弹的股票",
                MinChangePercent = -10,
                MaxChangePercent = -3,
                MinPE = 8,
                MaxPE = 30,
                IsDefault = false
            }
        };
        
        context.ScreenTemplates.AddRange(screenTemplates);
        context.SaveChanges();
    }
}

