namespace ADFlowManager.Core.Models;

/// <summary>
/// Représente un utilisateur Active Directory.
/// </summary>
public class User
{
    /// <summary>
    /// Nom d'utilisateur (SamAccountName).
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Nom complet affiché (DisplayName).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Adresse email de l'utilisateur.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Prénom de l'utilisateur.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Nom de famille de l'utilisateur.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Distinguished Name (DN) complet de l'utilisateur dans AD.
    /// Ex: CN=John Doe,OU=Users,DC=contoso,DC=local
    /// </summary>
    public string DistinguishedName { get; set; } = string.Empty;

    /// <summary>
    /// Indique si le compte utilisateur est activé.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Description de l'utilisateur.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// User Principal Name (UPN) - ex: john.doe@contoso.local
    /// </summary>
    public string UserPrincipalName { get; set; } = string.Empty;

    /// <summary>
    /// Numéro de téléphone fixe.
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Numéro de téléphone mobile.
    /// </summary>
    public string Mobile { get; set; } = string.Empty;

    /// <summary>
    /// Intitulé du poste.
    /// </summary>
    public string JobTitle { get; set; } = string.Empty;

    /// <summary>
    /// Service / Département.
    /// </summary>
    public string Department { get; set; } = string.Empty;

    /// <summary>
    /// Nom de l'entreprise.
    /// </summary>
    public string Company { get; set; } = string.Empty;

    /// <summary>
    /// Bureau / Site.
    /// </summary>
    public string Office { get; set; } = string.Empty;

    /// <summary>
    /// Liste des groupes auxquels appartient l'utilisateur.
    /// </summary>
    public List<Group> Groups { get; set; } = new();
}
