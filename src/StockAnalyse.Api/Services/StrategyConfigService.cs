using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace StockAnalyse.Api.Services;

/// <summary>
/// 策略配置服务实现
/// </summary>
public class StrategyConfigService : IStrategyConfigService
{
    private readonly ILogger<StrategyConfigService> _logger;
    private readonly string _configPath;
    private readonly StockDbContext _context;
    private readonly JsonSerializerOptions _jsonOptions;

    public StrategyConfigService(
        ILogger<StrategyConfigService> logger, 
        IWebHostEnvironment env,
        StockDbContext context)
    {
        _logger = logger;
        _configPath = Path.Combine(env.ContentRootPath, "strategy-configs");
        _context = context;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        // 确保配置目录存在
        EnsureConfigDirectoryExists();
    }

    /// <summary>
    /// 确保配置目录存在
    /// </summary>
    private void EnsureConfigDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_configPath))
            {
                Directory.CreateDirectory(_configPath);
                _logger.LogInformation("创建策略配置目录: {Path}", _configPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建策略配置目录失败: {Path}", _configPath);
        }
    }

    /// <summary>
    /// 加载所有默认策略配置
    /// </summary>
    public async Task<List<Models.StrategyConfig>> LoadDefaultStrategiesAsync()
    {
        var strategies = new List<Models.StrategyConfig>();
        
        try
        {
            if (!Directory.Exists(_configPath))
            {
                _logger.LogWarning("策略配置目录不存在: {Path}", _configPath);
                return strategies;
            }

            var configFiles = Directory.GetFiles(_configPath, "*.json");
            _logger.LogInformation("找到 {Count} 个策略配置文件", configFiles.Length);
            
            foreach (var file in configFiles)
            {
                try
                {
                    var config = await LoadConfigFromFileAsync(file);
                    if (config != null)
                    {
                        strategies.Add(config);
                        _logger.LogDebug("成功加载策略配置: {Name}", config.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载策略配置文件失败: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载默认策略配置失败");
        }

        return strategies;
    }

    /// <summary>
    /// 根据名称加载策略配置
    /// </summary>
    public async Task<Models.StrategyConfig?> LoadStrategyByNameAsync(string strategyName)
    {
        try
        {
            var fileName = $"{strategyName.ToLower().Replace(" ", "-")}.json";
            var filePath = Path.Combine(_configPath, fileName);
            
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("策略配置文件不存在: {File}", filePath);
                return null;
            }

            return await LoadConfigFromFileAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "根据名称加载策略配置失败: {Name}", strategyName);
            return null;
        }
    }

    /// <summary>
    /// 保存策略配置到文件
    /// </summary>
    public async Task<bool> SaveStrategyConfigAsync(Models.StrategyConfig config, string? fileName = null)
    {
        try
        {
            fileName ??= $"{config.Name.ToLower().Replace(" ", "-")}.json";
            var filePath = Path.Combine(_configPath, fileName);
            
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("策略配置保存成功: {File}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存策略配置失败: {Name}", config.Name);
            return false;
        }
    }

    /// <summary>
    /// 删除策略配置文件
    /// </summary>
    public Task<bool> DeleteStrategyConfigAsync(string fileName)
    {
        try
        {
            var filePath = Path.Combine(_configPath, fileName);
            
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("要删除的策略配置文件不存在: {File}", filePath);
                return Task.FromResult(false);
            }

            File.Delete(filePath);
            _logger.LogInformation("策略配置文件删除成功: {File}", filePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除策略配置文件失败: {File}", fileName);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 获取所有可用的策略配置文件名
    /// </summary>
    public Task<List<string>> GetAvailableConfigFilesAsync()
    {
        try
        {
            if (!Directory.Exists(_configPath))
            {
                return Task.FromResult(new List<string>());
            }

            var files = Directory.GetFiles(_configPath, "*.json")
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();

            return Task.FromResult(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取可用配置文件列表失败");
            return Task.FromResult(new List<string>());
        }
    }

    /// <summary>
    /// 将策略配置转换为量化策略实体
    /// </summary>
    public QuantStrategy ConvertToQuantStrategy(Models.StrategyConfig config)
    {
        return new QuantStrategy
        {
            Name = config.Name,
            Description = config.Description,
            Type = config.Type,
            Parameters = JsonSerializer.Serialize(config.Parameters, _jsonOptions),
            InitialCapital = config.InitialCapital,
            CurrentCapital = config.InitialCapital,
            IsActive = config.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 批量导入策略配置到数据库
    /// </summary>
    public async Task<int> ImportStrategiesToDatabaseAsync()
    {
        try
        {
            var configs = await LoadDefaultStrategiesAsync();
            var importedCount = 0;

            foreach (var config in configs)
            {
                // 检查策略是否已存在
                var existingStrategy = await _context.QuantStrategies
                    .FirstOrDefaultAsync(s => s.Name == config.Name);

                if (existingStrategy == null)
                {
                    var strategy = ConvertToQuantStrategy(config);
                    _context.QuantStrategies.Add(strategy);
                    importedCount++;
                    _logger.LogInformation("导入新策略: {Name}", config.Name);
                }
                else
                {
                    _logger.LogDebug("策略已存在，跳过: {Name}", config.Name);
                }
            }

            if (importedCount > 0)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("成功导入 {Count} 个策略到数据库", importedCount);
            }

            return importedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量导入策略配置到数据库失败");
            return 0;
        }
    }

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    private async Task<Models.StrategyConfig?> LoadConfigFromFileAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<Models.StrategyConfig>(json, _jsonOptions);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从文件加载配置失败: {File}", filePath);
            return null;
        }
    }
}