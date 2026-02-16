using System.Text;
using System.Text.Json;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using ADFlowManager.Infrastructure.Data;
using ADFlowManager.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.Services;

/// <summary>
/// Service d'audit : trace toutes les actions AD dans SQLite (local ou réseau).
/// </summary>
public class AuditService : IAuditService
{
    private const int MaxRetryAttempts = 3;

    private readonly ISettingsService _settingsService;
    private readonly ILogger<AuditService> _logger;
    private readonly string _currentUsername;

    public AuditService(
        ISettingsService settingsService,
        ILogger<AuditService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        _currentUsername = Environment.UserName;

        EnsureDatabaseCreated();
    }

    private void EnsureDatabaseCreated()
    {
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                var dbPath = GetDatabasePath();
                var directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                using var db = new AuditDbContext(dbPath);
                db.Database.EnsureCreated();
                db.ConfigureForConcurrency();

                _logger.LogInformation("Audit DB initialisée : {Path}", dbPath);
                return;
            }
            catch (Exception ex) when (IsTransientSqliteError(ex) && attempt < MaxRetryAttempts)
            {
                var delayMs = 250 * attempt;
                _logger.LogWarning(ex,
                    "Erreur transitoire création DB audit (tentative {Attempt}/{Max}) - retry dans {DelayMs}ms",
                    attempt,
                    MaxRetryAttempts,
                    delayMs);
                Thread.Sleep(delayMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur création DB audit");
                return;
            }
        }
    }

    private string GetDatabasePath()
    {
        var settings = _settingsService.CurrentSettings.Audit;
        var isNetworkMode = settings.StorageMode == "Network";

        var configuredPath = isNetworkMode && !string.IsNullOrWhiteSpace(settings.NetworkDatabasePath)
            ? settings.NetworkDatabasePath
            : settings.LocalDatabasePath;

        return NormalizeDatabasePath(configuredPath, isNetworkMode);
    }

    private static string NormalizeDatabasePath(string configuredPath, bool isNetworkMode)
    {
        var path = Environment.ExpandEnvironmentVariables(configuredPath.Trim());

        if (LooksLikeDirectoryPath(path))
        {
            var fileName = isNetworkMode ? "audit-shared.db" : "audit.db";
            return Path.Combine(path, fileName);
        }

        return path;
    }

    private static bool LooksLikeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        if (path.EndsWith("\\") || path.EndsWith("/"))
            return true;

        if (Directory.Exists(path))
            return true;

        return !Path.HasExtension(path);
    }

    private async Task ExecuteWithRetryAsync(Func<Task> action, string operationName)
    {
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (IsTransientSqliteError(ex) && attempt < MaxRetryAttempts)
            {
                var delayMs = 250 * attempt;
                _logger.LogWarning(ex,
                    "Erreur transitoire audit ({Operation}) tentative {Attempt}/{Max} - retry dans {DelayMs}ms",
                    operationName,
                    attempt,
                    MaxRetryAttempts,
                    delayMs);
                await Task.Delay(delayMs);
            }
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, string operationName)
    {
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsTransientSqliteError(ex) && attempt < MaxRetryAttempts)
            {
                var delayMs = 250 * attempt;
                _logger.LogWarning(ex,
                    "Erreur transitoire audit ({Operation}) tentative {Attempt}/{Max} - retry dans {DelayMs}ms",
                    operationName,
                    attempt,
                    MaxRetryAttempts,
                    delayMs);
                await Task.Delay(delayMs);
            }
        }

        return await action();
    }

    private static bool IsTransientSqliteError(Exception ex)
    {
        return ex is SqliteException or IOException or TimeoutException;
    }

    public async Task LogAsync(
        string actionType,
        string entityType,
        string entityId,
        string entityDisplayName,
        object? details = null,
        bool success = true,
        string? errorMessage = null)
    {
        try
        {
            if (!_settingsService.CurrentSettings.Audit.IsEnabled)
                return;

            await ExecuteWithRetryAsync(async () =>
            {
                var dbPath = GetDatabasePath();
                using var db = new AuditDbContext(dbPath);
                db.ConfigureForConcurrency();

                var logEntity = new AuditLogEntity
                {
                    Timestamp = DateTime.Now,
                    Username = _currentUsername,
                    ActionType = actionType,
                    EntityType = entityType,
                    EntityId = entityId,
                    EntityDisplayName = entityDisplayName,
                    Details = details != null ? JsonSerializer.Serialize(details) : "{}",
                    Result = success ? "Success" : "Failed",
                    ErrorMessage = errorMessage
                };

                db.AuditLogs.Add(logEntity);
                await db.SaveChangesAsync();
            }, "LogAsync");

            _logger.LogInformation("Audit: {Action} {Entity} {Id} by {User}",
                actionType, entityType, entityId, _currentUsername);
        }
        catch (Exception ex)
        {
            // Audit ne doit jamais bloquer les actions métier
            _logger.LogError(ex, "Erreur log audit");
        }
    }

    public async Task<List<AuditLog>> GetLogsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? username = null,
        string? actionType = null,
        string? entityType = null,
        string? entityId = null,
        int limit = 1000)
    {
        try
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var dbPath = GetDatabasePath();
                using var db = new AuditDbContext(dbPath);
                db.ConfigureForConcurrency();

                var query = db.AuditLogs.AsQueryable();

                if (startDate.HasValue)
                    query = query.Where(a => a.Timestamp >= startDate.Value);
                if (endDate.HasValue)
                    query = query.Where(a => a.Timestamp <= endDate.Value);
                if (!string.IsNullOrWhiteSpace(username))
                    query = query.Where(a => a.Username.Contains(username));
                if (!string.IsNullOrWhiteSpace(actionType))
                    query = query.Where(a => a.ActionType == actionType);
                if (!string.IsNullOrWhiteSpace(entityType))
                    query = query.Where(a => a.EntityType == entityType);
                if (!string.IsNullOrWhiteSpace(entityId))
                    query = query.Where(a => a.EntityId == entityId);

                query = query.OrderByDescending(a => a.Timestamp);
                if (limit > 0)
                    query = query.Take(limit);

                var entities = await query
                    .AsNoTracking()
                    .ToListAsync();

                return entities.Select(MapToModel).ToList();
            }, "GetLogsAsync");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur récupération logs audit");
            return [];
        }
    }

    public async Task<List<AuditLog>> GetEntityLogsAsync(string entityType, string entityId, int limit = 100)
    {
        return await GetLogsAsync(entityType: entityType, entityId: entityId, limit: limit);
    }

    public async Task ExportToCsvAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var logs = await GetLogsAsync(startDate: startDate, endDate: endDate, limit: 100000);

            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,Username,Action,EntityType,EntityId,EntityDisplayName,Result,ErrorMessage");

            foreach (var log in logs)
            {
                csv.AppendLine(
                    $"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                    $"\"{log.Username}\"," +
                    $"\"{log.ActionType}\"," +
                    $"\"{log.EntityType}\"," +
                    $"\"{log.EntityId}\"," +
                    $"\"{log.EntityDisplayName}\"," +
                    $"\"{log.Result}\"," +
                    $"\"{log.ErrorMessage}\"");
            }

            await File.WriteAllTextAsync(filePath, csv.ToString(), Encoding.UTF8);
            _logger.LogInformation("Audit exporté CSV : {Path} ({Count} lignes)", filePath, logs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur export CSV audit");
            throw;
        }
    }

    public async Task PurgeOldLogsAsync(int retentionDays)
    {
        try
        {
            if (retentionDays <= 0) return;

            await ExecuteWithRetryAsync(async () =>
            {
                var dbPath = GetDatabasePath();
                using var db = new AuditDbContext(dbPath);
                db.ConfigureForConcurrency();

                var cutoff = DateTime.Now.AddDays(-retentionDays);
                var deleted = await db.AuditLogs
                    .Where(a => a.Timestamp < cutoff)
                    .ExecuteDeleteAsync();

                if (deleted > 0)
                    _logger.LogInformation("Purge audit : {Count} logs supprimés (> {Days}j)", deleted, retentionDays);
            }, "PurgeOldLogsAsync");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur purge logs audit");
        }
    }

    public async Task<AuditStats> GetStatsAsync()
    {
        try
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var dbPath = GetDatabasePath();
                using var db = new AuditDbContext(dbPath);
                db.ConfigureForConcurrency();

                var now = DateTime.Now;
                var today = now.Date;
                var weekAgo = now.AddDays(-7);

                return new AuditStats
                {
                    TotalLogs = await db.AuditLogs.CountAsync(),
                    LogsToday = await db.AuditLogs.CountAsync(a => a.Timestamp >= today),
                    LogsThisWeek = await db.AuditLogs.CountAsync(a => a.Timestamp >= weekAgo),
                    OldestLog = await db.AuditLogs.OrderBy(a => a.Timestamp).Select(a => (DateTime?)a.Timestamp).FirstOrDefaultAsync(),
                    NewestLog = await db.AuditLogs.OrderByDescending(a => a.Timestamp).Select(a => (DateTime?)a.Timestamp).FirstOrDefaultAsync()
                };
            }, "GetStatsAsync");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur stats audit");
            return new AuditStats();
        }
    }

    private static AuditLog MapToModel(AuditLogEntity e) => new()
    {
        Id = e.Id,
        Timestamp = e.Timestamp,
        Username = e.Username,
        ActionType = e.ActionType,
        EntityType = e.EntityType,
        EntityId = e.EntityId,
        EntityDisplayName = e.EntityDisplayName,
        Details = e.Details,
        Result = e.Result,
        ErrorMessage = e.ErrorMessage
    };
}
