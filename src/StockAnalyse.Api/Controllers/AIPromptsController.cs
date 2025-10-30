using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIPromptsController : ControllerBase
{
    private readonly StockDbContext _context;
    private readonly ILogger<AIPromptsController> _logger;

    public AIPromptsController(StockDbContext context, ILogger<AIPromptsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AIPrompt>>> GetAll()
    {
        var list = await _context.AIPrompts.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Id).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AIPrompt>> Get(int id)
    {
        var prompt = await _context.AIPrompts.FindAsync(id);
        return prompt == null ? NotFound() : Ok(prompt);
    }

    [HttpGet("default")]
    public async Task<ActionResult<AIPrompt?>> GetDefault()
    {
        var prompt = await _context.AIPrompts.FirstOrDefaultAsync(p => p.IsDefault);
        return Ok(prompt);
    }

    [HttpPost]
    public async Task<ActionResult<AIPrompt>> Create([FromBody] AIPrompt prompt)
    {
        if (prompt.IsDefault)
        {
            foreach (var p in _context.AIPrompts)
            {
                p.IsDefault = false;
            }
        }

        _context.AIPrompts.Add(prompt);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = prompt.Id }, prompt);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, [FromBody] AIPrompt dto)
    {
        var prompt = await _context.AIPrompts.FindAsync(id);
        if (prompt == null) return NotFound();

        prompt.Name = dto.Name;
        prompt.SystemPrompt = dto.SystemPrompt;
        prompt.Temperature = dto.Temperature;
        prompt.IsActive = dto.IsActive;

        if (dto.IsDefault)
        {
            foreach (var p in _context.AIPrompts)
            {
                p.IsDefault = false;
            }
            prompt.IsDefault = true;
        }
        else
        {
            prompt.IsDefault = false;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var prompt = await _context.AIPrompts.FindAsync(id);
        if (prompt == null) return NotFound();

        _context.AIPrompts.Remove(prompt);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}