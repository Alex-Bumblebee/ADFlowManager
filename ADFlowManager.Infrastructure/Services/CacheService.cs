using System.Text.Json;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using ADFlowManager.Infrastructure.Data;
using ADFlowManager.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.Services;

/// <summary>
/// Service de cache SQLite local pour accélérer le chargement des users/groupes AD.
/// TTL par défaut : 120 minutes. Base : %APPDATA%\ADFlowManager\Cache\adflow-cache.db
/// </summary>
public class CacheService : ICacheService
{
    private readonly ILogger<CacheService> _logger;
    private readonly ISettingsService _settingsService;
    private const int DefaultTTL = 120;
    private const string SchemaVersionKey = "schema_version";
    private const string CurrentSchemaVersion = "4"; // Incrémenter à chaque changement de schéma

    public CacheService(ILogger<CacheService> logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;

        try
        {
            using var db = new CacheDbContext();
            db.Database.EnsureCreated();

            // Vérifier la version du schéma - si différente, on recréé la DB
            var meta = db.CacheMetadata.FirstOrDefault(m => m.Key == SchemaVersionKey);
            if (meta == null || meta.ItemCount.ToString() != CurrentSchemaVersion)
            {
                _logger.LogInformation("Schéma cache obsolète → suppression et recréation");
                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();

                db.CacheMetadata.Add(new Data.Entities.CacheMetadata
                {
                    Key = SchemaVersionKey,
                    LastRefresh = DateTime.Now,
                    ItemCount = int.Parse(CurrentSchemaVersion)
                });
                db.SaveChanges();
            }

            _logger.LogInformation("Cache SQLite initialisé (schéma v{Version})", CurrentSchemaVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur initialisation cache SQLite");
        }
    }

    public async Task<List<User>?> GetCachedUsersAsync()
    {
        try
        {
            using var db = new CacheDbContext();

            var ttl = (int)_settingsService.CurrentSettings.Cache.TtlMinutes;
            if (!await IsCacheValidInternalAsync(db, "users", ttl))
            {
                _logger.LogInformation("Cache users expiré (> {TTL} min)", ttl);
                return null;
            }

            var cachedUsers = await db.CachedUsers.AsNoTracking().ToListAsync();

            if (cachedUsers.Count == 0)
            {
                _logger.LogInformation("Cache users vide");
                return null;
            }

            var users = cachedUsers.Select(cu => new User
            {
                UserName = cu.UserName,
                DisplayName = cu.DisplayName,
                FirstName = cu.FirstName,
                LastName = cu.LastName,
                Email = cu.Email,
                UserPrincipalName = cu.UserPrincipalName,
                Phone = cu.Phone,
                Mobile = cu.Mobile,
                JobTitle = cu.JobTitle,
                Department = cu.Department,
                Company = cu.Company,
                Office = cu.Office,
                Description = cu.Description,
                DistinguishedName = cu.DistinguishedName,
                IsEnabled = cu.IsEnabled,
                Groups = DeserializeGroups(cu.GroupsJson)
            }).ToList();

            _logger.LogInformation("Cache users valide : {Count} users chargés", users.Count);
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lecture cache users");
            return null;
        }
    }

    public async Task<List<Group>?> GetCachedGroupsAsync()
    {
        try
        {
            using var db = new CacheDbContext();

            var ttl = (int)_settingsService.CurrentSettings.Cache.TtlMinutes;
            if (!await IsCacheValidInternalAsync(db, "groups", ttl))
            {
                _logger.LogInformation("Cache groups expiré (> {TTL} min)", ttl);
                return null;
            }

            var cachedGroups = await db.CachedGroups.AsNoTracking().ToListAsync();

            if (cachedGroups.Count == 0)
            {
                _logger.LogInformation("Cache groups vide");
                return null;
            }

            var groups = cachedGroups.Select(cg => new Group
            {
                GroupName = cg.GroupName,
                Description = cg.Description,
                DistinguishedName = cg.DistinguishedName
            }).ToList();

            _logger.LogInformation("Cache groups valide : {Count} groups chargés", groups.Count);
            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lecture cache groups");
            return null;
        }
    }

    public async Task CacheUsersAsync(List<User> users)
    {
        try
        {
            _logger.LogInformation("Mise en cache de {Count} users...", users.Count);

            using var db = new CacheDbContext();

            await db.CachedUsers.ExecuteDeleteAsync();

            var now = DateTime.Now;
            var cachedUsers = users.Select(u => new CachedUser
            {
                UserName = u.UserName,
                DisplayName = u.DisplayName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                UserPrincipalName = u.UserPrincipalName,
                Phone = u.Phone,
                Mobile = u.Mobile,
                JobTitle = u.JobTitle,
                Department = u.Department,
                Company = u.Company,
                Office = u.Office,
                Description = u.Description,
                DistinguishedName = u.DistinguishedName,
                IsEnabled = u.IsEnabled,
                GroupsJson = SerializeGroups(u.Groups),
                CachedAt = now
            }).ToList();

            await db.CachedUsers.AddRangeAsync(cachedUsers);

            var metadata = await db.CacheMetadata.FindAsync("users");
            if (metadata == null)
            {
                metadata = new CacheMetadata { Key = "users" };
                db.CacheMetadata.Add(metadata);
            }
            metadata.LastRefresh = now;
            metadata.ItemCount = users.Count;

            await db.SaveChangesAsync();

            _logger.LogInformation("Cache users mis à jour : {Count} users", users.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur mise en cache users");
        }
    }

    public async Task CacheGroupsAsync(List<Group> groups)
    {
        try
        {
            _logger.LogInformation("Mise en cache de {Count} groups...", groups.Count);

            using var db = new CacheDbContext();

            await db.CachedGroups.ExecuteDeleteAsync();

            var now = DateTime.Now;
            var cachedGroups = groups.Select(g => new CachedGroup
            {
                GroupName = g.GroupName,
                Description = g.Description,
                DistinguishedName = g.DistinguishedName,
                CachedAt = now
            }).ToList();

            await db.CachedGroups.AddRangeAsync(cachedGroups);

            var metadata = await db.CacheMetadata.FindAsync("groups");
            if (metadata == null)
            {
                metadata = new CacheMetadata { Key = "groups" };
                db.CacheMetadata.Add(metadata);
            }
            metadata.LastRefresh = now;
            metadata.ItemCount = groups.Count;

            await db.SaveChangesAsync();

            _logger.LogInformation("Cache groups mis à jour : {Count} groups", groups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur mise en cache groups");
        }
    }

    public async Task CacheUserAsync(User user)
    {
        try
        {
            using var db = new CacheDbContext();

            var existing = await db.CachedUsers.FindAsync(user.UserName);
            if (existing != null)
            {
                existing.DisplayName = user.DisplayName;
                existing.FirstName = user.FirstName;
                existing.LastName = user.LastName;
                existing.Email = user.Email;
                existing.UserPrincipalName = user.UserPrincipalName;
                existing.Phone = user.Phone;
                existing.Mobile = user.Mobile;
                existing.JobTitle = user.JobTitle;
                existing.Department = user.Department;
                existing.Company = user.Company;
                existing.Office = user.Office;
                existing.Description = user.Description;
                existing.DistinguishedName = user.DistinguishedName;
                existing.IsEnabled = user.IsEnabled;
                existing.GroupsJson = SerializeGroups(user.Groups);
                existing.CachedAt = DateTime.Now;
            }
            else
            {
                db.CachedUsers.Add(new CachedUser
                {
                    UserName = user.UserName,
                    DisplayName = user.DisplayName,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    UserPrincipalName = user.UserPrincipalName,
                    Phone = user.Phone,
                    Mobile = user.Mobile,
                    JobTitle = user.JobTitle,
                    Department = user.Department,
                    Company = user.Company,
                    Office = user.Office,
                    Description = user.Description,
                    DistinguishedName = user.DistinguishedName,
                    IsEnabled = user.IsEnabled,
                    GroupsJson = SerializeGroups(user.Groups),
                    CachedAt = DateTime.Now
                });
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Cache user mis à jour : {UserName}", user.UserName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur mise à jour cache user {UserName}", user.UserName);
        }
    }

    public async Task ClearCacheAsync()
    {
        try
        {
            _logger.LogInformation("Vidage du cache SQLite...");

            using var db = new CacheDbContext();

            await db.CachedUsers.ExecuteDeleteAsync();
            await db.CachedGroups.ExecuteDeleteAsync();
            await db.CacheMetadata.ExecuteDeleteAsync();

            _logger.LogInformation("Cache vidé avec succès");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur vidage cache");
        }
    }

    public async Task<bool> IsCacheValidAsync(string cacheKey, int ttlMinutes = DefaultTTL)
    {
        try
        {
            using var db = new CacheDbContext();
            return await IsCacheValidInternalAsync(db, cacheKey, ttlMinutes);
        }
        catch
        {
            return false;
        }
    }

    public async Task<CacheStats> GetCacheStatsAsync()
    {
        var stats = new CacheStats();

        try
        {
            using var db = new CacheDbContext();

            var usersMeta = await db.CacheMetadata.AsNoTracking().FirstOrDefaultAsync(m => m.Key == "users");
            var groupsMeta = await db.CacheMetadata.AsNoTracking().FirstOrDefaultAsync(m => m.Key == "groups");

            if (usersMeta != null)
            {
                stats.UsersLastRefresh = usersMeta.LastRefresh;
                stats.UsersCount = usersMeta.ItemCount;
            }

            if (groupsMeta != null)
            {
                stats.GroupsLastRefresh = groupsMeta.LastRefresh;
                stats.GroupsCount = groupsMeta.ItemCount;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lecture stats cache");
        }

        return stats;
    }

    private async Task<bool> IsCacheValidInternalAsync(CacheDbContext db, string cacheKey, int ttlMinutes = DefaultTTL)
    {
        var metadata = await db.CacheMetadata.AsNoTracking().FirstOrDefaultAsync(m => m.Key == cacheKey);

        if (metadata == null)
            return false;

        var age = DateTime.Now - metadata.LastRefresh;
        var isValid = age.TotalMinutes < ttlMinutes;

        if (!isValid)
        {
            _logger.LogInformation("Cache {Key} expiré : {Age} min (TTL: {TTL} min)",
                cacheKey, (int)age.TotalMinutes, ttlMinutes);
        }

        return isValid;
    }

    private static string SerializeGroups(List<Group> groups)
    {
        var simplified = groups.Select(g => new
        {
            g.GroupName,
            g.Description,
            g.DistinguishedName
        });
        return JsonSerializer.Serialize(simplified);
    }

    private static List<Group> DeserializeGroups(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
                return [];

            var items = JsonSerializer.Deserialize<List<GroupDto>>(json);
            return items?.Select(i => new Group
            {
                GroupName = i.GroupName ?? "",
                Description = i.Description ?? "",
                DistinguishedName = i.DistinguishedName ?? ""
            }).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// DTO interne pour la désérialisation JSON des groupes.
    /// </summary>
    private class GroupDto
    {
        public string? GroupName { get; set; }
        public string? Description { get; set; }
        public string? DistinguishedName { get; set; }
    }
}
