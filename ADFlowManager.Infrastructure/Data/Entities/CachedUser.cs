using System.ComponentModel.DataAnnotations;

namespace ADFlowManager.Infrastructure.Data.Entities;

public class CachedUser
{
    [Key]
    public string UserName { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string UserPrincipalName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Department { get; set; } = "";
    public string Company { get; set; } = "";
    public string Office { get; set; } = "";
    public string Description { get; set; } = "";
    public string DistinguishedName { get; set; } = "";

    public bool IsEnabled { get; set; }

    /// <summary>
    /// Groupes sérialisés en JSON (List of {GroupName, Description, DistinguishedName}).
    /// </summary>
    public string GroupsJson { get; set; } = "[]";

    public DateTime CachedAt { get; set; }
}
