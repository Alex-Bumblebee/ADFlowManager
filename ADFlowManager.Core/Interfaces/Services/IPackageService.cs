using ADFlowManager.Core.Models.Deployment;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service de gestion des packages de déploiement logiciel.
/// Stockage JSON local et réseau optionnel.
/// </summary>
public interface IPackageService
{
    /// <summary>
    /// Récupère tous les packages disponibles (local + réseau).
    /// </summary>
    Task<IEnumerable<DeploymentPackage>> GetPackagesAsync();

    /// <summary>
    /// Récupère un package par son identifiant.
    /// </summary>
    /// <param name="id">Identifiant du package</param>
    Task<DeploymentPackage?> GetPackageByIdAsync(string id);

    /// <summary>
    /// Crée un nouveau package.
    /// </summary>
    /// <param name="package">Package à créer</param>
    /// <returns>Package créé avec ID généré</returns>
    Task<DeploymentPackage> CreatePackageAsync(DeploymentPackage package);

    /// <summary>
    /// Met à jour un package existant.
    /// </summary>
    /// <param name="package">Package avec les modifications</param>
    Task UpdatePackageAsync(DeploymentPackage package);

    /// <summary>
    /// Supprime un package.
    /// </summary>
    /// <param name="id">Identifiant du package à supprimer</param>
    Task DeletePackageAsync(string id);

    /// <summary>
    /// Importe un package depuis un fichier JSON.
    /// </summary>
    /// <param name="filePath">Chemin du fichier JSON</param>
    /// <returns>Package importé</returns>
    Task<DeploymentPackage> ImportPackageAsync(string filePath);

    /// <summary>
    /// Exporte un package vers un fichier JSON.
    /// </summary>
    /// <param name="package">Package à exporter</param>
    /// <param name="filePath">Chemin de destination</param>
    Task ExportPackageAsync(DeploymentPackage package, string filePath);
}
