using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Services;
using StockAnalyse.Api.Services.Interfaces;
using System.Text;

// 注册CodePages编码提供程序，支持GBK等中文编码
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// 添加响应压缩服务（支持大响应）
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

// 配置压缩选项
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Optimal;
});

builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Optimal;
});

// 添加服务
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        // 配置为camelCase命名策略，与前端保持一致
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        // 增加JSON序列化大小限制，支持大响应
        options.JsonSerializerOptions.MaxDepth = 64;
    });

// 配置Kestrel服务器选项，增加请求/响应大小限制
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
    options.Limits.MaxResponseBufferSize = 100 * 1024 * 1024; // 100MB
});

// 配置请求大小限制
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100 * 1024 * 1024; // 100MB
});
    
// 添加内存缓存服务（用于缓存选股结果）
builder.Services.AddMemoryCache();
    
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "股票分析API", 
        Version = "v1",
        Description = "一个功能完整的股票分析系统"
    });
});

// 配置数据库
builder.Services.AddDbContext<StockDbContext>(options =>
{
    options.UseSqlite("Data Source=stockanalyse.db");
    // 禁用敏感数据日志和SQL查询日志
    options.EnableSensitiveDataLogging(false);
    if (!builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors(false);
    }
});

// 注册服务
builder.Services.AddScoped<IStockDataService, StockDataService>();
builder.Services.AddScoped<IWatchlistService, WatchlistService>();
builder.Services.AddScoped<IScreenService, ScreenService>();
builder.Services.AddScoped<INewsService, NewsService>();
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<IPriceAlertService, PriceAlertService>();
builder.Services.AddSingleton<NewsConfigService>();
builder.Services.AddSingleton<AIPromptConfigService>();

// 量化交易服务
builder.Services.AddScoped<IQuantTradingService, QuantTradingService>();
builder.Services.AddScoped<ITechnicalIndicatorService, TechnicalIndicatorService>();
builder.Services.AddScoped<IBacktestService, BacktestService>();
builder.Services.AddScoped<IStrategyConfigService, StrategyConfigService>();
builder.Services.AddScoped<IStrategyOptimizationService, StrategyOptimizationService>();

// 注册定时任务服务
builder.Services.AddHostedService<NewsBackgroundService>();

// 添加HttpClient，为AI服务配置专门的HttpClient，设置更长的超时时间（5分钟）
builder.Services.AddHttpClient("AIService", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5); // 5分钟超时
});

// 默认HttpClient（用于其他服务）
builder.Services.AddHttpClient();

// 配置CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 初始化数据库（将 EnsureCreated 替换为 Migrate）
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<StockDbContext>();
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "应用迁移失败，回退到 EnsureCreated");
        context.Database.EnsureCreated();
    }

    DatabaseSeeder.Seed(context);
}

// 配置HTTP请求管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseResponseCompression(); // 启用响应压缩（支持大响应）
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();

// 默认页面重定向到index.html
app.MapFallbackToFile("index.html");

// 定时任务：每分钟检查一次自选股建议价格提醒
var timer = new System.Timers.Timer(60000); // 60秒
timer.Elapsed += async (sender, e) =>
{
    using var scope = app.Services.CreateScope();
    var alertService = scope.ServiceProvider.GetRequiredService<IPriceAlertService>();
    // 检查自选股建议价格提醒
    await alertService.CheckSuggestedPriceAlertsAsync();
};
timer.Start();

app.Run();


