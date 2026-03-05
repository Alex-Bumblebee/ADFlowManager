using ADFlowManager.Core.Models;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service de gestion des ordinateurs Active Directory.
/// </summary>
public interface IComputerService
{
    /// <summary>
    /// Récupère tous les ordinateurs du domaine avec filtre optionnel.
    /// </summary>
    /// <param name="searchFilter">Filtre de recherche (nom). Vide = tous les ordinateurs</param>
    /// <returns>Liste des ordinateurs trouvés</returns>
    Task<IEnumerable<Computer>> GetComputersAsync(string? searchFilter = null);

    /// <summary>
    /// Récupère un ordinateur par son nom.
    /// </summary>
    /// <param name="computerName">Nom de l'ordinateur</param>
    /// <returns>L'ordinateur trouvé, ou null si inexistant</returns>
    Task<Computer?> GetComputerByNameAsync(string computerName);

    /// <summary>
    /// Vérifie si un ordinateur est en ligne (ping).
    /// </summary>
    /// <param name="computerName">Nom de l'ordinateur</param>
    /// <returns>True si l'ordinateur répond au ping</returns>
    Task<bool> IsComputerOnlineAsync(string computerName);

    /// <summary>
    /// Récupère les informations système d'un ordinateur distant.
    /// </summary>
    /// <param name="computerName">Nom de l'ordinateur</param>
    /// <returns>Informations système, ou null si inaccessible</returns>
    Task<ComputerSystemInfo?> GetSystemInfoAsync(string computerName);
}
