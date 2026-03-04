namespace ADFlowManager.Core.Models.Deployment;

/// <summary>
/// Catégorisation de l'erreur de déploiement pour l'affichage et les actions correctives.
/// </summary>
public enum DeploymentErrorCategory
{
    /// <summary>Aucune erreur (succès).</summary>
    None = 0,
    /// <summary>IOException sur File.Copy — fichier installeur déjà verrouillé sur la cible (installation précédente encore en cours).</summary>
    FileLocked,
    /// <summary>Timeout dépassé et installeur inactif à la vérification.</summary>
    Timeout,
    /// <summary>Timeout dépassé mais l'installeur était encore actif — installation probablement réussie.</summary>
    TimeoutProbablyOk,
    /// <summary>Code retour non nul de l'installeur.</summary>
    InstallerNonZeroExit,
    /// <summary>Échec connexion réseau (SMB/RPC).</summary>
    ConnectionError,
    /// <summary>Hash SHA-256 ou signature ECDSA invalide.</summary>
    IntegrityError,
    /// <summary>Autre exception non catégorisée.</summary>
    Other
}

/// <summary>
/// Résultat du déploiement d'un package sur un ordinateur.
/// </summary>
public class DeploymentResult
{
    /// <summary>
    /// Nom de l'ordinateur cible.
    /// </summary>
    public string ComputerName { get; set; } = string.Empty;

    /// <summary>
    /// Identifiant du package déployé.
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// Nom du package déployé.
    /// </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Indique si le déploiement a réussi.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Message d'erreur en cas d'échec.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Heure de début du déploiement.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Heure de fin du déploiement.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Durée totale du déploiement.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Code retour de l'installeur (0 = succès, 3010 = succès + reboot requis).
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Version du package déployé.
    /// </summary>
    public string PackageVersion { get; set; } = string.Empty;

    /// <summary>
    /// Indique que le déploiement a échoué à cause d'un timeout (délai expiré avant fin de l'installeur).
    /// </summary>
    public bool IsTimeout { get; set; }

    /// <summary>
    /// Indique que le fichier installeur était encore verrouillé au moment du timeout
    /// (l'installeur tournait probablement encore — l'installation a peut-être réussi).
    /// </summary>
    public bool InstallerWasRunning { get; set; }

    /// <summary>
    /// Timeout effectif utilisé lors du déploiement (en secondes). Utile pour proposer de l'augmenter.
    /// </summary>
    public int TimeoutUsedSeconds { get; set; }

    /// <summary>
    /// Catégorie de l'erreur — permet un affichage et des actions correctives ciblés.
    /// </summary>
    public DeploymentErrorCategory ErrorCategory { get; set; } = DeploymentErrorCategory.None;

    /// <summary>
    /// Fichiers résiduels laissés sur la cible après un nettoyage partiel.
    /// Non vide = nettoyage manuel requis.
    /// </summary>
    public List<string> ResidualFiles { get; set; } = new();

    /// <summary>
    /// Résultats détaillés par étape.
    /// </summary>
    public List<StepResult> Steps { get; set; } = new();
}

/// <summary>
/// Résultat d'une étape de déploiement.
/// </summary>
public class StepResult
{
    public int Order { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Progression du déploiement (pour IProgress).
/// </summary>
public class DeploymentProgress
{
    /// <summary>
    /// Étape courante (Connexion, Copie, Installation...).
    /// </summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Pourcentage de progression (0-100).
    /// </summary>
    public int Percentage { get; set; }

    /// <summary>
    /// Message descriptif.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
