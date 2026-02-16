using System.ComponentModel.DataAnnotations;

namespace ADFlowManager.Infrastructure.Data.Entities;

/// <summary>
/// Entité EF Core pour les logs d'audit.
/// </summary>
public class AuditLogEntity
{
    [Key]
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Username { get; set; } = "";
    public string ActionType { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityDisplayName { get; set; } = "";
    public string Details { get; set; } = "{}";
    public string Result { get; set; } = "Success";
    public string? ErrorMessage { get; set; }
}
