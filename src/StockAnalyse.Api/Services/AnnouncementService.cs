using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;
using System.Text.Json;

namespace StockAnalyse.Api.Services
{
    /// <summary>
    /// 公告服务实现
    /// </summary>
    public class AnnouncementService : IAnnouncementService
    {
        private readonly StockDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AnnouncementService> _logger;
        private const string PYTHON_SERVICE_URL = "http://localhost:5001";

        public AnnouncementService(
            StockDbContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<AnnouncementService> logger)
        {
            _context = context;
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
        }

        public async Task<List<StockAnnouncement>> GetRecentAnnouncementsAsync(string stockCode, int days = 30)
        {
            // 1. 检查缓存（1天内的数据）
            var cached = await _context.StockAnnouncements
                .Where(a => a.StockCode == stockCode)
                .Where(a => a.UpdateTime > DateTime.Now.AddDays(-1))
                .Where(a => a.PublishDate > DateTime.Now.AddDays(-days))
                .OrderByDescending(a => a.PublishDate)
                .ToListAsync();

            if (cached.Any())
            {
                _logger.LogInformation("从缓存获取公告: {StockCode}, {Count}条", stockCode, cached.Count);
                return cached;
            }

            // 2. 从Python服务获取
            await RefreshAnnouncementDataAsync(stockCode, days);

            // 3. 返回最新数据
            return await _context.StockAnnouncements
                .Where(a => a.StockCode == stockCode)
                .Where(a => a.PublishDate > DateTime.Now.AddDays(-days))
                .OrderByDescending(a => a.PublishDate)
                .ToListAsync();
        }

        public async Task<bool> HasNegativeAnnouncementAsync(string stockCode, int days = 7)
        {
            var announcements = await GetRecentAnnouncementsAsync(stockCode, days);
            return announcements.Any(a => a.IsNegative);
        }

        public async Task<bool> RefreshAnnouncementDataAsync(string stockCode, int days = 30)
        {
            try
            {
                var url = $"{PYTHON_SERVICE_URL}/api/stock/announcements/{stockCode}?days={days}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("获取公告失败: {StockCode}, Status: {StatusCode}",
                        stockCode, response.StatusCode);
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<AnnouncementApiResponse>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Success != true || result.Data == null)
                {
                    _logger.LogWarning("公告数据格式错误: {StockCode}", stockCode);
                    return false;
                }

                // 保存到数据库
                foreach (var item in result.Data.Announcements)
                {
                    var announcement = new StockAnnouncement
                    {
                        StockCode = stockCode,
                        Title = item.Title,
                        Type = item.Type,
                        PublishDate = string.IsNullOrEmpty(item.PublishDate) ? null : DateTime.Parse(item.PublishDate),
                        IsNegative = item.IsNegative,
                        RiskKeywords = item.Keywords != null && item.Keywords.Any()
                            ? JsonSerializer.Serialize(item.Keywords)
                            : null,
                        UpdateTime = DateTime.Now
                    };

                    // 检查是否已存在（基于标题和日期）
                    var exists = await _context.StockAnnouncements
                        .AnyAsync(a => a.StockCode == stockCode &&
                                     a.Title == announcement.Title &&
                                     a.PublishDate == announcement.PublishDate);

                    if (!exists)
                    {
                        await _context.StockAnnouncements.AddAsync(announcement);
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("成功刷新公告: {StockCode}, {Count}条记录",
                    stockCode, result.Data.Announcements.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新公告失败: {StockCode}", stockCode);
                return false;
            }
        }

        // DTO类
        private class AnnouncementApiResponse
        {
            public bool Success { get; set; }
            public AnnouncementData? Data { get; set; }
        }

        private class AnnouncementData
        {
            public List<AnnouncementItem> Announcements { get; set; } = new();
        }

        private class AnnouncementItem
        {
            public string Title { get; set; } = string.Empty;
            public string? Type { get; set; }
            public string? PublishDate { get; set; }
            public bool IsNegative { get; set; }
            public List<string>? Keywords { get; set; }
        }
    }
}
