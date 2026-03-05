using System.ComponentModel.DataAnnotations;

namespace ADFlowManager.Infrastructure.Data.Entities;

public class CachedComputer
{
    [Key]
    public string Name { get; set; } = "";

    public string DistinguishedName { get; set; } = "";
    public string Description { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string OperatingSystemVersion { get; set; } = "";
    public DateTime? LastLogon { get; set; }
    public bool Enabled { get; set; }
    public string Location { get; set; } = "";
    public string ManagedBy { get; set; } = "";

    /// <summary>
    /// MemberOf sérialisé en JSON (List&lt;string&gt;).
    /// </summary>
    public string MemberOfJson { get; set; } = "[]";

    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public string IPv4Address { get; set; } = "";
    public string MACAddress { get; set; } = "";

    public DateTime CachedAt { get; set; }
}
