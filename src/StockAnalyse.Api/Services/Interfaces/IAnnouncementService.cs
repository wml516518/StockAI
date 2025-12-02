using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Services.Interfaces
{
    /// <summary>
    /// 公告服务接口
    /// </summary>
    public interface IAnnouncementService
    {
        /// <summary>
        /// 获取股票最近的公告
        /// </summary>
        Task<List<StockAnnouncement>> GetRecentAnnouncementsAsync(string stockCode, int days = 30);

        /// <summary>
        /// 检查股票最近是否有负面公告
        /// </summary>
        Task<bool> HasNegativeAnnouncementAsync(string stockCode, int days = 7);

        /// <summary>
        /// 刷新股票的公告数据（从Python服务获取）
        /// </summary>
        Task<bool> RefreshAnnouncementDataAsync(string stockCode, int days = 30);
    }
}
