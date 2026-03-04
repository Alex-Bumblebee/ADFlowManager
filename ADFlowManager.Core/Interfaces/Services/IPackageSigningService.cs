using ADFlowManager.Core.Models.Deployment;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service de signature ECDSA des packages de déploiement.
/// Utilise une clé ECDSA P-256 stockée dans le magasin de certificats Windows (CurrentUser\My).
/// Signe le triplet "{InstallerHash}|{Name}|{Version}" pour garantir l'intégrité et l'authenticité.
/// </summary>
public interface IPackageSigningService
{
    /// <summary>
    /// Vérifie si une clé de signature est disponible dans le magasin de certificats.
    /// </summary>
    bool IsSigningKeyAvailable();

    /// <summary>
    /// Génère une nouvelle paire de clés ECDSA P-256 et la stocke dans le magasin de certificats.
    /// Si une clé existe déjà, elle est remplacée.
    /// </summary>
    void GetOrCreateSigningKey();

    /// <summary>
    /// Signe un package avec la clé ECDSA.
    /// Met à jour la propriété Signature du package avec la signature Base64.
    /// </summary>
    /// <param name="package">Le package à signer</param>
    /// <returns>True si la signature a réussi, false si aucune clé disponible</returns>
    bool SignPackage(DeploymentPackage package);

    /// <summary>
    /// Vérifie la signature d'un package.
    /// </summary>
    /// <param name="package">Le package à vérifier</param>
    /// <returns>True si la signature est valide, false sinon</returns>
    bool VerifyPackage(DeploymentPackage package);

    /// <summary>
    /// Exporte la clé de signature (certificat + clé privée) vers un fichier PFX.
    /// </summary>
    /// <param name="pfxPath">Chemin du fichier PFX de sortie</param>
    /// <param name="password">Mot de passe de protection du PFX</param>
    void ExportSigningKey(string pfxPath, string password);

    /// <summary>
    /// Importe une clé de signature depuis un fichier PFX.
    /// Remplace la clé existante si présente.
    /// </summary>
    /// <param name="pfxPath">Chemin du fichier PFX</param>
    /// <param name="password">Mot de passe du PFX</param>
    void ImportSigningKey(string pfxPath, string password);

    /// <summary>
    /// Supprime la clé de signature du magasin de certificats.
    /// </summary>
    void DeleteSigningKey();

    /// <summary>
    /// Récupère l'empreinte (thumbprint SHA-1) de la clé de signature actuelle.
    /// </summary>
    /// <returns>Thumbprint ou null si aucune clé</returns>
    string? GetSigningKeyThumbprint();
}
