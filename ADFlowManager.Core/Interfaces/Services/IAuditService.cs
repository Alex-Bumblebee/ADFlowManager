using ADFlowManager.Core.Models;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service d'audit : trace toutes les actions AD dans SQLite (local ou réseau).
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logger une action audit.
    /// </summary>
    Task LogAsync(
        string actionType,
        string entityType,
        string entityId,
        string entityDisplayName,
        object? details = null,
        bool success = true,
        string? errorMessage = null);

    /// <summary>
    /// Récupérer logs audit avec filtres.
    /// </summary>
    Task<List<AuditLog>> GetLogsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? username = null,
        string? actionType = null,
        string? entityType = null,
        string? entityId = null,
        int limit = 1000);

    /// <summary>
    /// Récupérer logs pour entité spécifique.
    /// </summary>
    Task<List<AuditLog>> GetEntityLogsAsync(string entityType, string entityId, int limit = 100);

    /// <summary>
    /// Exporter logs vers CSV.
    /// </summary>
    Task ExportToCsvAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Purger logs anciens (rétention).
    /// </summary>
    Task PurgeOldLogsAsync(int retentionDays);

    /// <summary>
    /// Obtenir statistiques audit.
    /// </summary>
    Task<AuditStats> GetStatsAsync();
}

/// <summary>
/// Statistiques audit pour affichage UI.
/// </summary>
public class AuditStats
{
    public int TotalLogs { get; set; }
    public int LogsToday { get; set; }
    public int LogsThisWeek { get; set; }
    public DateTime? OldestLog { get; set; }
    public DateTime? NewestLog { get; set; }
}
