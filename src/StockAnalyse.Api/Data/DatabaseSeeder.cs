using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Data;

public static class DatabaseSeeder
{
    public static void Seed(StockDbContext context)
    {
        // 检查是否已有分类数据，如果没有则创建默认分类
        if (!context.WatchlistCategories.Any())
        {
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
        }
        
        // 检查是否已有AI模型配置，如果没有则添加默认配置
        if (!context.AIModelConfigs.Any())
        {
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
        }
        
        // 添加默认选股模板（根据市场行情优化）
        // 定义所有应该存在的模板
        var requiredTemplates = new Dictionary<string, ScreenTemplate>
        {
            ["低价成长股"] = new ScreenTemplate
            {
                Name = "低价成长股",
                Description = "价格较低、成长性好的中小盘股票。适合寻找有成长潜力的投资标的。\n\n参数说明：\n- 价格5-30元：处于合理低价区间\n- 换手率2%-8%：有一定活跃度但不过度炒作\n- 成交量>5000手：保证流动性\n- 市值50-500亿：中小盘成长股典型规模\n- 股息率0-3%：成长股通常股息率不高（利润用于再投资）\n- PE 10-40：合理的估值区间\n- PB 1-5：合理的市净率\n- 涨跌幅-5%到+10%：有一定上涨空间",
                MinPrice = 5,
                MaxPrice = 30,
                MinTurnoverRate = 2,      // 换手率最低2%，保证有一定活跃度
                MaxTurnoverRate = 8,      // 换手率最高8%，避免过度炒作
                MinVolume = 5000,         // 成交量最低5000手，保证流动性（单位：手）
                MinMarketValue = 500000,   // 市值最低50亿（单位：万元）
                MaxMarketValue = 5000000, // 市值最高500亿，中小盘成长股
                MinDividendYield = 0,     // 股息率最低0%
                MaxDividendYield = 3,     // 股息率最高3%，成长股通常不高
                MinPE = 10,               // PE最低10，避免高估值
                MaxPE = 40,               // PE最高40，合理成长股估值
                MinPB = 1,                // PB最低1，避免过度低估
                MaxPB = 5,                // PB最高5，合理市净率
                MinChangePercent = -5,    // 涨跌幅最低-5%
                MaxChangePercent = 10,    // 涨跌幅最高+10%
                IsDefault = true
            },
            ["高股息蓝筹"] = new ScreenTemplate
            {
                Name = "高股息蓝筹",
                Description = "股息率较高、估值合理的蓝筹股票。适合稳健型投资者。\n\n参数说明：\n- 价格>10元：相对成熟的公司\n- 股息率>3%：较高分红回报\n- PE<20：估值合理\n- 市值>100亿：大盘蓝筹股\n- PB 0.8-3：合理市净率",
                MinPrice = 10,
                MinDividendYield = 3,
                MaxDividendYield = 10,
                MinPE = 5,
                MaxPE = 20,
                MinPB = 0.8m,
                MaxPB = 3,
                MinMarketValue = 1000000, // 100亿以上（单位：万元）
                IsDefault = false
            },
            ["活跃小盘股"] = new ScreenTemplate
            {
                Name = "活跃小盘股",
                Description = "换手率高、市值较小的活跃股票。适合短线操作。\n\n参数说明：\n- 价格<50元：中小盘股\n- 换手率>5%：交易活跃\n- 市值<50亿：小盘股\n- 涨跌幅>0%：近期上涨",
                MaxPrice = 50,
                MinTurnoverRate = 5,
                MaxTurnoverRate = 20,
                MinVolume = 5000,
                MaxMarketValue = 500000, // 50亿以下（单位：万元）
                MinChangePercent = 0,
                MaxChangePercent = 15,
                IsDefault = false
            },
            ["超跌反弹"] = new ScreenTemplate
            {
                Name = "超跌反弹",
                Description = "近期跌幅较大，可能反弹的股票。适合抄底策略。\n\n参数说明：\n- 涨跌幅-10%到-3%：近期明显下跌\n- PE 8-30：估值合理，避免垃圾股\n- PB 0.5-3：市净率合理\n- 换手率>1%：保证有一定流动性",
                MinChangePercent = -10,
                MaxChangePercent = -3,
                MinPE = 8,
                MaxPE = 30,
                MinPB = 0.5m,
                MaxPB = 3,
                MinTurnoverRate = 1,
                MaxTurnoverRate = 15,
                IsDefault = false
            },
            ["优质白马股"] = new ScreenTemplate
            {
                Name = "优质白马股",
                Description = "基本面优秀、估值合理的白马股。适合长期投资。\n\n参数说明：\n- 价格10-100元：成熟公司\n- PE 10-25：估值合理\n- PB 1-3：市净率适中\n- 市值>500亿：大盘股\n- 股息率0-5%：可能有分红",
                MinPrice = 10,
                MaxPrice = 100,
                MinPE = 10,
                MaxPE = 25,
                MinPB = 1,
                MaxPB = 3,
                MinMarketValue = 5000000, // 500亿以上（单位：万元）
                MinDividendYield = 0,
                MaxDividendYield = 5,
                IsDefault = false
            },
            ["高成长潜力股"] = new ScreenTemplate
            {
                Name = "高成长潜力股",
                Description = "市值较小但成长性高的潜力股。适合寻找高成长标的。\n\n参数说明：\n- 市值10-100亿：小中盘股\n- PE 15-50：成长股估值\n- 换手率3%-10%：有一定关注度\n- 涨跌幅0%-15%：上涨趋势",
                MaxPrice = 50,
                MinMarketValue = 100000,   // 10亿以上（单位：万元）
                MaxMarketValue = 10000000, // 100亿以下（单位：万元）
                MinPE = 15,
                MaxPE = 50,
                MinTurnoverRate = 3,
                MaxTurnoverRate = 10,
                MinChangePercent = 0,
                MaxChangePercent = 15,
                IsDefault = false
            }
        };
        
        // 检查并添加缺失的模板
        var existingTemplates = context.ScreenTemplates.ToList();
        var existingNames = existingTemplates.Select(t => t.Name).ToHashSet();
        
        foreach (var templatePair in requiredTemplates)
        {
            if (!existingNames.Contains(templatePair.Key))
            {
                // 模板不存在，添加它
                context.ScreenTemplates.Add(templatePair.Value);
            }
        }

        // 检查并添加AI提示词
        var requiredPrompts = new List<AIPrompt>
        {
            new AIPrompt
            {
                Name = "基本面分析",
                SystemPrompt = "你是一名资深的A股分析师。请结合财务数据、技术指标、消息面、行业地位，对指定股票进行结构化分析，并给出风险提示与操作建议。",
                Temperature = 0.7,
                IsDefault = true,
                IsActive = true
            },
            new AIPrompt
            {
                Name = "新闻分析",
                SystemPrompt = "你是一名资深的金融新闻分析师。请分析新闻内容对市场的影响，重点关注：1. 新闻涉及的股票和行业；2. 可能对市场的影响；3. 投资机会和风险提示。请给出专业的分析意见。",
                Temperature = 0.7,
                IsDefault = false,
                IsActive = true
            },
            new AIPrompt
            {
                Name = "技术分析",
                SystemPrompt = "你是一名专业的技术分析师。请结合K线形态、成交量、技术指标（如MACD、RSI等），对股票的走势进行分析，并给出支撑位、压力位和操作建议。",
                Temperature = 0.7,
                IsDefault = false,
                IsActive = true
            },
            new AIPrompt
            {
                Name = "综合分析",
                SystemPrompt = "你是一名资深的投资顾问。请综合基本面、新闻舆论和技术面分析结果，对股票{stockCode}进行总结，给出清晰的整体判断、风险提示与操作建议。",
                Temperature = 0.7,
                IsDefault = false,
                IsActive = true
            }
        };

        var existingPromptNames = context.AIPrompts.Select(p => p.Name).ToHashSet();
        foreach (var prompt in requiredPrompts)
        {
            if (!existingPromptNames.Contains(prompt.Name))
            {
                context.AIPrompts.Add(prompt);
            }
        }
        
        context.SaveChanges();
    }
}

