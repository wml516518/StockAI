using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Services;
using StockAnalyse.Api.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// 添加服务
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
    
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
    options.UseSqlite("Data Source=stockanalyse.db"));

// 注册服务
builder.Services.AddScoped<IStockDataService, StockDataService>();
builder.Services.AddScoped<IWatchlistService, WatchlistService>();
builder.Services.AddScoped<IScreenService, ScreenService>();
builder.Services.AddScoped<INewsService, NewsService>();
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<IPriceAlertService, PriceAlertService>();
builder.Services.AddSingleton<NewsConfigService>();
builder.Services.AddSingleton<AIPromptConfigService>();

// 注册定时任务服务
builder.Services.AddHostedService<NewsBackgroundService>();

// 添加HttpClient
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

// 初始化数据库
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<StockDbContext>();
    context.Database.EnsureCreated();
    
    // 初始化默认数据
    DatabaseSeeder.Seed(context);
}

// 配置HTTP请求管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();

// 默认页面重定向到index.html
app.MapFallbackToFile("index.html");

// 定时任务：每分钟检查一次价格提醒
var timer = new System.Timers.Timer(60000); // 60秒
timer.Elapsed += async (sender, e) =>
{
    using var scope = app.Services.CreateScope();
    var alertService = scope.ServiceProvider.GetRequiredService<IPriceAlertService>();
    await alertService.CheckAndTriggerAlertsAsync();
};
timer.Start();

app.Run();


