using System.ComponentModel.DataAnnotations;

namespace ADFlowManager.Infrastructure.Data.Entities;

public class CacheMetadata
{
    [Key]
    public string Key { get; set; } = "";

    public DateTime LastRefresh { get; set; }
    public int ItemCount { get; set; }
}
