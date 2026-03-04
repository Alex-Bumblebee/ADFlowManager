using ADFlowManager.Core.Models.Deployment;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service de déploiement de packages logiciels sur des ordinateurs distants.
/// Utilise SMB (partage admin$) + SCM (Service Control Manager) distant.
/// Aucune configuration requise sur le PC cible (pas de WinRM, pas de CIM/WMI).
/// </summary>
public interface IDeploymentService
{
    /// <summary>
    /// Déploie un package sur un ordinateur distant.
    /// </summary>
    /// <param name="computerName">Nom de l'ordinateur cible</param>
    /// <param name="package">Package à déployer</param>
    /// <param name="progress">Progression optionnelle</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Résultat du déploiement</returns>
    Task<DeploymentResult> DeployPackageAsync(
        string computerName,
        DeploymentPackage package,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Déploie un package sur plusieurs ordinateurs en parallèle.
    /// </summary>
    /// <param name="computerNames">Noms des ordinateurs cibles</param>
    /// <param name="package">Package à déployer</param>
    /// <param name="maxParallel">Nombre maximum de déploiements simultanés</param>
    /// <param name="progress">Progression optionnelle globale</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Résultats de chaque déploiement</returns>
    Task<IEnumerable<DeploymentResult>> DeployPackageBatchAsync(
        IEnumerable<string> computerNames,
        DeploymentPackage package,
        int maxParallel = 5,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Déploie plusieurs packages sur plusieurs ordinateurs.
    /// Chaque package est déployé séquentiellement ; pour chaque package, tous les ordinateurs
    /// sont traités en parallèle (limité par maxParallel).
    /// </summary>
    /// <param name="computerNames">Noms des ordinateurs cibles</param>
    /// <param name="packages">Packages à déployer dans l'ordre</param>
    /// <param name="maxParallel">Déploiements simultanés par package</param>
    /// <param name="progress">Progression optionnelle</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Résultats de tous les déploiements (package × ordinateur)</returns>
    Task<IEnumerable<DeploymentResult>> DeployMultiplePackagesBatchAsync(
        IEnumerable<string> computerNames,
        IEnumerable<DeploymentPackage> packages,
        int maxParallel = 5,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
