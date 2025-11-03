using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScreenTemplateController : ControllerBase
{
    private readonly StockDbContext _context;
    private readonly ILogger<ScreenTemplateController> _logger;

    public ScreenTemplateController(StockDbContext context, ILogger<ScreenTemplateController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有选股模板
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ScreenTemplate>>> GetAll()
    {
        var templates = await _context.ScreenTemplates
            .OrderByDescending(t => t.IsDefault)
            .ThenByDescending(t => t.UpdateTime)
            .ToListAsync();
        return Ok(templates);
    }

    /// <summary>
    /// 根据ID获取选股模板
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ScreenTemplate>> GetById(int id)
    {
        var template = await _context.ScreenTemplates.FindAsync(id);
        if (template == null)
        {
            return NotFound();
        }
        return Ok(template);
    }

    /// <summary>
    /// 创建选股模板
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ScreenTemplate>> Create([FromBody] ScreenTemplate template)
    {
        template.CreateTime = DateTime.Now;
        template.UpdateTime = DateTime.Now;
        
        _context.ScreenTemplates.Add(template);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("创建选股模板: {Name}", template.Name);
        return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
    }

    /// <summary>
    /// 更新选股模板
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ScreenTemplate template)
    {
        if (id != template.Id)
        {
            return BadRequest();
        }

        var existingTemplate = await _context.ScreenTemplates.FindAsync(id);
        if (existingTemplate == null)
        {
            return NotFound();
        }

        // 更新字段
        existingTemplate.Name = template.Name;
        existingTemplate.Description = template.Description;
        existingTemplate.MinPrice = template.MinPrice;
        existingTemplate.MaxPrice = template.MaxPrice;
        existingTemplate.MinChangePercent = template.MinChangePercent;
        existingTemplate.MaxChangePercent = template.MaxChangePercent;
        existingTemplate.MinTurnoverRate = template.MinTurnoverRate;
        existingTemplate.MaxTurnoverRate = template.MaxTurnoverRate;
        existingTemplate.MinVolume = template.MinVolume;
        existingTemplate.MaxVolume = template.MaxVolume;
        existingTemplate.MinMarketValue = template.MinMarketValue;
        existingTemplate.MaxMarketValue = template.MaxMarketValue;
        existingTemplate.MinPE = template.MinPE;
        existingTemplate.MaxPE = template.MaxPE;
        existingTemplate.MinPB = template.MinPB;
        existingTemplate.MaxPB = template.MaxPB;
        existingTemplate.MinDividendYield = template.MinDividendYield;
        existingTemplate.MaxDividendYield = template.MaxDividendYield;
        existingTemplate.MinCirculatingShares = template.MinCirculatingShares;
        existingTemplate.MaxCirculatingShares = template.MaxCirculatingShares;
        existingTemplate.MinTotalShares = template.MinTotalShares;
        existingTemplate.MaxTotalShares = template.MaxTotalShares;
        existingTemplate.Market = template.Market;
        existingTemplate.IsDefault = template.IsDefault;
        existingTemplate.UpdateTime = DateTime.Now;

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("更新选股模板: {Name}", template.Name);
        return NoContent();
    }

    /// <summary>
    /// 删除选股模板
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var template = await _context.ScreenTemplates.FindAsync(id);
        if (template == null)
        {
            return NotFound();
        }

        _context.ScreenTemplates.Remove(template);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("删除选股模板: {Name}", template.Name);
        return NoContent();
    }

    /// <summary>
    /// 设置默认模板
    /// </summary>
    [HttpPost("{id}/set-default")]
    public async Task<IActionResult> SetDefault(int id)
    {
        // 清除所有默认标记
        var allTemplates = await _context.ScreenTemplates.ToListAsync();
        foreach (var t in allTemplates)
        {
            t.IsDefault = false;
        }

        // 设置新的默认模板
        var template = await _context.ScreenTemplates.FindAsync(id);
        if (template == null)
        {
            return NotFound();
        }

        template.IsDefault = true;
        template.UpdateTime = DateTime.Now;
        
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("设置默认选股模板: {Name}", template.Name);
        return NoContent();
    }

    /// <summary>
    /// 从模板创建选股条件
    /// </summary>
    [HttpGet("{id}/to-criteria")]
    public async Task<ActionResult<ScreenCriteria>> ToCriteria(int id)
    {
        var template = await _context.ScreenTemplates.FindAsync(id);
        if (template == null)
        {
            return NotFound();
        }

        var criteria = new ScreenCriteria
        {
            Market = template.Market,
            MinPrice = template.MinPrice,
            MaxPrice = template.MaxPrice,
            MinChangePercent = template.MinChangePercent,
            MaxChangePercent = template.MaxChangePercent,
            MinTurnoverRate = template.MinTurnoverRate,
            MaxTurnoverRate = template.MaxTurnoverRate,
            MinVolume = template.MinVolume,
            MaxVolume = template.MaxVolume,
            MinMarketValue = template.MinMarketValue,
            MaxMarketValue = template.MaxMarketValue,
            MinPE = template.MinPE,
            MaxPE = template.MaxPE,
            MinPB = template.MinPB,
            MaxPB = template.MaxPB,
            MinDividendYield = template.MinDividendYield,
            MaxDividendYield = template.MaxDividendYield,
            MinCirculatingShares = template.MinCirculatingShares,
            MaxCirculatingShares = template.MaxCirculatingShares,
            MinTotalShares = template.MinTotalShares,
            MaxTotalShares = template.MaxTotalShares
        };

        return Ok(criteria);
    }

    /// <summary>
    /// 初始化或更新优化后的默认模板（根据市场行情优化）
    /// </summary>
    [HttpPost("initialize-optimized")]
    public async Task<IActionResult> InitializeOptimizedTemplates()
    {
        var templates = await _context.ScreenTemplates.ToListAsync();
        
        // 定义优化的模板配置
        var optimizedTemplates = new Dictionary<string, ScreenTemplate>
        {
            ["低价成长股"] = new ScreenTemplate
            {
                Name = "低价成长股",
                Description = "价格较低、成长性好的中小盘股票。适合寻找有成长潜力的投资标的。\n\n参数说明：\n- 价格5-30元：处于合理低价区间\n- 换手率2%-8%：有一定活跃度但不过度炒作\n- 成交量>5000手：保证流动性\n- 市值50-500亿：中小盘成长股典型规模\n- 股息率0-3%：成长股通常股息率不高（利润用于再投资）\n- PE 10-40：合理的估值区间\n- PB 1-5：合理的市净率\n- 涨跌幅-5%到+10%：有一定上涨空间",
                MinPrice = 5,
                MaxPrice = 30,
                MinTurnoverRate = 2,
                MaxTurnoverRate = 8,
                MinVolume = 5000,
                MinMarketValue = 500000,
                MaxMarketValue = 5000000,
                MinDividendYield = 0,
                MaxDividendYield = 3,
                MinPE = 10,
                MaxPE = 40,
                MinPB = 1,
                MaxPB = 5,
                MinChangePercent = -5,
                MaxChangePercent = 10,
                IsDefault = true,
                CreateTime = DateTime.Now,
                UpdateTime = DateTime.Now
            }
        };

        int updatedCount = 0;
        int createdCount = 0;

        foreach (var optimizedTemplate in optimizedTemplates)
        {
            var existing = templates.FirstOrDefault(t => t.Name == optimizedTemplate.Key);
            if (existing != null)
            {
                // 更新现有模板
                existing.Description = optimizedTemplate.Value.Description;
                existing.MinPrice = optimizedTemplate.Value.MinPrice;
                existing.MaxPrice = optimizedTemplate.Value.MaxPrice;
                existing.MinTurnoverRate = optimizedTemplate.Value.MinTurnoverRate;
                existing.MaxTurnoverRate = optimizedTemplate.Value.MaxTurnoverRate;
                existing.MinVolume = optimizedTemplate.Value.MinVolume;
                existing.MinMarketValue = optimizedTemplate.Value.MinMarketValue;
                existing.MaxMarketValue = optimizedTemplate.Value.MaxMarketValue;
                existing.MinDividendYield = optimizedTemplate.Value.MinDividendYield;
                existing.MaxDividendYield = optimizedTemplate.Value.MaxDividendYield;
                existing.MinPE = optimizedTemplate.Value.MinPE;
                existing.MaxPE = optimizedTemplate.Value.MaxPE;
                existing.MinPB = optimizedTemplate.Value.MinPB;
                existing.MaxPB = optimizedTemplate.Value.MaxPB;
                existing.MinChangePercent = optimizedTemplate.Value.MinChangePercent;
                existing.MaxChangePercent = optimizedTemplate.Value.MaxChangePercent;
                existing.UpdateTime = DateTime.Now;
                updatedCount++;
                _logger.LogInformation("更新模板: {Name}", existing.Name);
            }
            else
            {
                // 创建新模板
                _context.ScreenTemplates.Add(optimizedTemplate.Value);
                createdCount++;
                _logger.LogInformation("创建模板: {Name}", optimizedTemplate.Value.Name);
            }
        }

        await _context.SaveChangesAsync();
        
        return Ok(new { 
            message = "模板初始化完成",
            updated = updatedCount,
            created = createdCount
        });
    }
}