using System.IO;

namespace ADFlowManager.Core.Models;

/// <summary>
/// Configuration globale application (extensible).
/// Persistée en JSON dans %APPDATA%\ADFlowManager\settings.json
/// </summary>
public class AppSettings
{
    public ActiveDirectorySettings ActiveDirectory { get; set; } = new();
    public UserCreationSettings UserCreation { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public LogSettings Logs { get; set; } = new();
    public AuditSettings Audit { get; set; } = new();
    public TemplateSettings Templates { get; set; } = new();
    public GeneralSettings General { get; set; } = new();

    /// <summary>
    /// Version config (pour migrations futures)
    /// </summary>
    public int ConfigVersion { get; set; } = 1;
}

public class UserCreationSettings
{
    /// <summary>
    /// Format du login (sAMAccountName / UPN).
    /// Valeurs possibles: "Prenom.Nom", "P.Nom", "Nom.P", "Nom"
    /// </summary>
    public string LoginFormat { get; set; } = "Prenom.Nom";

    /// <summary>
    /// Format du nom d'affichage (DisplayName).
    /// Valeurs possibles: "Prenom Nom", "Nom Prenom"
    /// </summary>
    public string DisplayNameFormat { get; set; } = "Prenom Nom";

    /// <summary>
    /// Gestion des doublons lors de la génération automatique du login.
    /// Valeurs possibles: "AppendNumber" (ajoute 1, 2...), "DoNothing" (laisse tel quel, risque d'erreur AD)
    /// </summary>
    public string DuplicateHandling { get; set; } = "AppendNumber";

    /// <summary>
    /// Domaine personnalisé pour l'email et l'UPN (ex: "exemple.fr").
    /// Si vide, utilise le domaine AD par défaut.
    /// </summary>
    public string EmailDomain { get; set; } = "";

    /// <summary>
    /// Politique de mot de passe appliquée côté UI.
    /// Valeurs possibles: "Easy", "Standard", "Strong"
    /// </summary>
    public string PasswordPolicy { get; set; } = "Standard";
}

public class ActiveDirectorySettings
{
    /// <summary>
    /// OU par défaut création utilisateurs.
    /// Exemple : "OU=Users,DC=contoso,DC=local"
    /// </summary>
    public string DefaultUserOU { get; set; } = "";

    /// <summary>
    /// OU par défaut création groupes.
    /// Exemple : "OU=Groups,DC=contoso,DC=local"
    /// </summary>
    public string DefaultGroupOU { get; set; } = "";

    /// <summary>
    /// OUs utilisateurs incluses (filter affichage).
    /// Liste vide = tous.
    /// </summary>
    public List<string> IncludedUserOUs { get; set; } = new();

    /// <summary>
    /// OUs utilisateurs exclues.
    /// </summary>
    public List<string> ExcludedUserOUs { get; set; } = new();

    /// <summary>
    /// OU de départ/désactivation.
    /// Si renseigné, l'utilisateur désactivé sera déplacé dans cette OU.
    /// Si réactivé, il sera remis dans DefaultUserOU (si renseigné).
    /// </summary>
    public string DisabledUserOU { get; set; } = "";

    /// <summary>
    /// Charger les groupes de chaque utilisateur au démarrage (GetGroups).
    /// Désactiver améliore les performances sur les grands domaines ou AD 2012 R2.
    /// Les groupes restent accessibles via le double-clic (chargement live).
    /// </summary>
    public bool LoadGroupsOnStartup { get; set; } = true;
}

public class CacheSettings
{
    /// <summary>
    /// Activer cache SQLite.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Durée de vie cache (TTL) en minutes.
    /// </summary>
    public double TtlMinutes { get; set; } = 120;
}

public class LogSettings
{
    /// <summary>
    /// Activer logs réseau (audit).
    /// </summary>
    public bool NetworkLogsEnabled { get; set; } = false;

    /// <summary>
    /// Chemin réseau pour logs audit.
    /// </summary>
    public string NetworkLogPath { get; set; } = "";
}

public class AuditSettings
{
    /// <summary>
    /// Activer audit logs.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Mode stockage : "Local" ou "Network".
    /// </summary>
    public string StorageMode { get; set; } = "Local";

    /// <summary>
    /// Chemin DB réseau (si Network).
    /// Exemple : "\\server\share\ADFlowManager\audit-shared.db"
    /// </summary>
    public string NetworkDatabasePath { get; set; } = "";

    /// <summary>
    /// Rétention logs en jours (0 = illimité).
    /// </summary>
    public int RetentionDays { get; set; } = 0;

    /// <summary>
    /// Chemin DB local (readonly, calculé).
    /// </summary>
    public string LocalDatabasePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADFlowManager",
            "Audit",
            "audit.db");
}

public class TemplateSettings
{
    /// <summary>
    /// Mode stockage : "Local" ou "Network".
    /// </summary>
    public string StorageMode { get; set; } = "Local";

    /// <summary>
    /// Dossier templates partagé (si mode Network).
    /// Exemple : "\\server\share\ADFlowManager\Templates"
    /// </summary>
    public string NetworkFolderPath { get; set; } = "";

    /// <summary>
    /// Dossier templates local (readonly, calculé).
    /// </summary>
    public string LocalFolderPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADFlowManager",
            "Templates");
}

public class GeneralSettings
{
    /// <summary>
    /// Index thème : 0=Sombre, 1=Clair
    /// </summary>
    public int ThemeIndex { get; set; } = 0;

    /// <summary>
    /// Index langue : 0=Français, 1=English
    /// </summary>
    public int LanguageIndex { get; set; } = 0;
}
