using ADFlowManager.Core.Models;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Interface pour le service de cache SQLite local.
/// Accélère le chargement des utilisateurs et groupes AD (TTL 120 min par défaut).
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Récupère les utilisateurs depuis le cache SQLite si valide.
    /// </summary>
    /// <returns>Liste des users cachés, ou null si cache expiré/vide.</returns>
    Task<List<User>?> GetCachedUsersAsync();

    /// <summary>
    /// Récupère les groupes depuis le cache SQLite si valide.
    /// </summary>
    /// <returns>Liste des groupes cachés, ou null si cache expiré/vide.</returns>
    Task<List<Group>?> GetCachedGroupsAsync();

    /// <summary>
    /// Met en cache la liste des utilisateurs.
    /// </summary>
    Task CacheUsersAsync(List<User> users);

    /// <summary>
    /// Met en cache la liste des groupes.
    /// </summary>
    Task CacheGroupsAsync(List<Group> groups);

    /// <summary>
    /// Met à jour (ou ajoute) un seul utilisateur dans le cache sans toucher aux autres.
    /// </summary>
    Task CacheUserAsync(User user);

    /// <summary>
    /// Vide intégralement le cache (users, groupes, metadata).
    /// </summary>
    Task ClearCacheAsync();

    /// <summary>
    /// Vérifie si le cache pour la clé donnée est encore valide (non expiré).
    /// </summary>
    /// <param name="cacheKey">Clé du cache ("users" ou "groups")</param>
    /// <param name="ttlMinutes">Durée de vie en minutes (défaut 120)</param>
    Task<bool> IsCacheValidAsync(string cacheKey, int ttlMinutes = 120);

    /// <summary>
    /// Récupère les stats du cache (dernière mise à jour, nombre d'éléments).
    /// </summary>
    Task<CacheStats> GetCacheStatsAsync();
}

/// <summary>
/// Statistiques du cache pour l'affichage dans Settings.
/// </summary>
public class CacheStats
{
    public DateTime? UsersLastRefresh { get; set; }
    public int UsersCount { get; set; }
    public DateTime? GroupsLastRefresh { get; set; }
    public int GroupsCount { get; set; }
}
