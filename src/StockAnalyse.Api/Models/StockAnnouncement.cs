using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalyse.Api.Models
{
    /// <summary>
    /// 股票公告数据模型
    /// 存储公告信息及负面关键词标记
    /// </summary>
    [Table("StockAnnouncements")]
    public class StockAnnouncement
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string StockCode { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Title { get; set; }

        [MaxLength(100)]
        public string? Type { get; set; }

        public string? Content { get; set; }

        public DateTime? PublishDate { get; set; }

        // 风险标记
        public bool IsNegative { get; set; }

        /// <summary>
        /// 匹配的风险关键词（JSON数组格式）
        /// </summary>
        public string? RiskKeywords { get; set; }

        // 元数据
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
