namespace ADFlowManager.Core.Models;

/// <summary>
/// Configuration des paramètres de logging pour Serilog.
/// Supporte logs locaux (obligatoires) et réseau (optionnels).
/// </summary>
public class LoggingSettings
{
    /// <summary>
    /// Chemin des logs locaux sur le disque de l'utilisateur.
    /// Par défaut : %LOCALAPPDATA%\ADFlowManager\logs\
    /// </summary>
    public string LocalLogPath { get; set; } = string.Empty;

    /// <summary>
    /// Chemin des logs réseau (optionnel) pour audit centralisé.
    /// Format attendu : \\serveur\share\logs\ ou null pour désactiver.
    /// </summary>
    public string? NetworkLogPath { get; set; }

    /// <summary>
    /// Niveau minimum de logging (Verbose, Debug, Information, Warning, Error, Fatal).
    /// Défaut : Information
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>
    /// Durée de rétention des logs locaux en jours.
    /// Défaut : 30 jours
    /// </summary>
    public int LocalRetentionDays { get; set; } = 30;

    /// <summary>
    /// Durée de rétention des logs réseau en jours.
    /// Plus long que local pour audit long terme.
    /// Défaut : 90 jours
    /// </summary>
    public int NetworkRetentionDays { get; set; } = 90;
}
