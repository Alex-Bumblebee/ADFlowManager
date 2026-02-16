using System.ComponentModel.DataAnnotations;

namespace ADFlowManager.Infrastructure.Data.Entities;

public class CachedGroup
{
    [Key]
    public string GroupName { get; set; } = "";

    public string Description { get; set; } = "";
    public string DistinguishedName { get; set; } = "";

    public DateTime CachedAt { get; set; }
}
