using System.Text.Json.Serialization;

namespace ADFlowManager.Core.Models.Deployment;

/// <summary>
/// Statut de vérification de la signature d'un package.
/// </summary>
public enum PackageSignatureStatus
{
    /// <summary>Non encore vérifié.</summary>
    Unknown,
    /// <summary>Signature valide.</summary>
    Valid,
    /// <summary>Signature présente mais invalide.</summary>
    Invalid,
    /// <summary>Aucune signature.</summary>
    Unsigned
}

/// <summary>
/// Package de déploiement logiciel.
/// Persisté en JSON dans le répertoire packages.
/// </summary>
public class DeploymentPackage
{
    /// <summary>
    /// Identifiant unique du package (GUID).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Nom du package.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version du package.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Description du package.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Catégorie (ex: Navigateurs, Utilitaires, Sécurité...).
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Informations sur l'installateur.
    /// </summary>
    public InstallerInfo Installer { get; set; } = new();

    /// <summary>
    /// Prérequis système.
    /// </summary>
    public PackageRequirements Requirements { get; set; } = new();

    /// <summary>
    /// Étapes de déploiement ordonnées.
    /// </summary>
    public List<DeploymentStep> Steps { get; set; } = new();

    /// <summary>
    /// Critères de succès post-installation.
    /// </summary>
    public SuccessCriteria? SuccessCriteria { get; set; }

    /// <summary>
    /// Auteur du package.
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Date de création.
    /// </summary>
    public DateTime Created { get; set; }

    /// <summary>
    /// Date de dernière modification.
    /// </summary>
    public DateTime Updated { get; set; }

    /// <summary>
    /// Tags pour filtrage/recherche.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Signature ECDSA du package (Base64).
    /// Signe "{InstallerHash}|{Name}|{Version}|{Arguments}|{Type}|{InstallerPath}|{StepsJson}" avec clé ECDSA P-256.
    /// Null si non signé (rétrocompatibilité).
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// Statut de vérification de la signature (calculé au chargement, non persisté).
    /// </summary>
    [JsonIgnore]
    public PackageSignatureStatus SignatureStatus { get; set; } = PackageSignatureStatus.Unknown;
}

/// <summary>
/// Informations sur l'installateur du package.
/// </summary>
public class InstallerInfo
{
    /// <summary>
    /// Type d'installateur : exe, msi, ps1.
    /// </summary>
    public string Type { get; set; } = "exe";

    /// <summary>
    /// Chemin local vers le fichier installateur.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Arguments de ligne de commande pour installation silencieuse.
    /// </summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Hash SHA-256 du fichier installateur (calculé à la création).
    /// Vérifié avant chaque déploiement pour garantir l'intégrité.
    /// </summary>
    public string InstallerHash { get; set; } = string.Empty;

    /// <summary>
    /// Timeout d'installation en secondes.
    /// 0 = calcul automatique basé sur la taille du fichier.
    /// </summary>
    public int TimeoutSeconds { get; set; }
}

/// <summary>
/// Prérequis système pour l'installation.
/// </summary>
public class PackageRequirements
{
    /// <summary>
    /// Version Windows minimale requise.
    /// </summary>
    public string MinWindowsVersion { get; set; } = string.Empty;

    /// <summary>
    /// Architecture requise : x86, x64, arm64.
    /// </summary>
    public string Architecture { get; set; } = "x64";

    /// <summary>
    /// Espace disque minimum en Mo.
    /// </summary>
    public int MinDiskSpaceMB { get; set; }

    /// <summary>
    /// RAM minimum en Mo.
    /// </summary>
    public int MinRamMB { get; set; }
}

/// <summary>
/// Étape de déploiement.
/// </summary>
public class DeploymentStep
{
    /// <summary>
    /// Ordre d'exécution.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Type d'étape : preCheck, install, postInstall, cleanup.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Action à exécuter.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Paramètres additionnels.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Critères de vérification post-installation.
/// </summary>
public class SuccessCriteria
{
    /// <summary>
    /// Type de vérification : fileExists, registryKey, exitCode.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Chemin à vérifier (fichier ou clé registre).
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Valeur attendue.
    /// </summary>
    public object? ExpectedValue { get; set; }
}
