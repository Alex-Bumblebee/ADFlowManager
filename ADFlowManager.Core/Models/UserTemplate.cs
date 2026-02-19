namespace ADFlowManager.Core.Models;

/// <summary>
/// Template utilisateur (configuration réutilisable pour la création rapide).
/// Stocké en JSON dans %APPDATA%\ADFlowManager\Templates\ ou dossier réseau partagé.
/// </summary>
public class UserTemplate
{
    /// <summary>
    /// ID unique template
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Nom template (ex: "Stagiaire IT", "Manager RH")
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Description template
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Créé par (username Windows)
    /// </summary>
    public string CreatedBy { get; set; } = Environment.UserName;

    /// <summary>
    /// Date création
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Date dernière modification
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    // ===== PROPRIÉTÉS ORGANISATION =====

    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public string? Company { get; set; }
    public string? Office { get; set; }
    public string? UserDescription { get; set; }

    /// <summary>
    /// OU de destination (Path LDAP). Si renseigné, sera pré-sélectionné à l'application du template.
    /// </summary>
    public string? DefaultOU { get; set; }

    // ===== GROUPES =====

    /// <summary>
    /// Liste noms groupes à assigner
    /// </summary>
    public List<string> Groups { get; set; } = new();

    // ===== OPTIONS =====

    /// <summary>
    /// Utilisateur doit changer password à la première connexion
    /// </summary>
    public bool MustChangePasswordAtLogon { get; set; } = true;

    /// <summary>
    /// Compte activé par défaut
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
