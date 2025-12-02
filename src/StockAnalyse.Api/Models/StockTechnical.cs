using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalyse.Api.Models
{
    /// <summary>
    /// 股票技术指标数据模型
    /// 存储MACD、KDJ、均线、ATR等技术指标
    /// </summary>
    [Table("StockTechnicals")]
    public class StockTechnical
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string StockCode { get; set; } = string.Empty;

        [Required]
        public DateTime TradeDate { get; set; }

        // 均线指标
        public decimal? MA5 { get; set; }
        public decimal? MA10 { get; set; }
        public decimal? MA20 { get; set; }
        public decimal? MA60 { get; set; }

        // MACD指标
        public decimal? MACD_DIF { get; set; }
        public decimal? MACD_DEA { get; set; }
        public decimal? MACD_HIST { get; set; }

        // KDJ指标
        public decimal? KDJ_K { get; set; }
        public decimal? KDJ_D { get; set; }
        public decimal? KDJ_J { get; set; }

        // 波动率
        public decimal? ATR { get; set; }

        // 成交量相关
        public decimal? Volume5DayAvg { get; set; }

        // 元数据
        public DateTime UpdateTime { get; set; } = DateTime.Now;

        // 索引：StockCode + TradeDate 唯一
    }
}
