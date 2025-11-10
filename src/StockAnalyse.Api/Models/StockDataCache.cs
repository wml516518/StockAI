using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalyse.Api.Models;

[Table("StockDataCaches")]
public class StockDataCache
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string StockCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string DataType { get; set; } = string.Empty;

    [Column(TypeName = "TEXT")]
    public string Payload { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? PayloadHash { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastRefreshedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAtUtc { get; set; }

    [Column(TypeName = "TEXT")]
    public string? Metadata { get; set; }

    public bool IsFallbackData { get; set; }
}


