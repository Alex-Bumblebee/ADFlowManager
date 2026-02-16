namespace ADFlowManager.Core.Models;

/// <summary>
/// Représente un groupe Active Directory.
/// </summary>
public class Group
{
    /// <summary>
    /// Nom du groupe (SamAccountName).
    /// </summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// Description du groupe.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Distinguished Name (DN) complet du groupe dans AD.
    /// Ex: CN=Admins,OU=Groups,DC=contoso,DC=local
    /// </summary>
    public string DistinguishedName { get; set; } = string.Empty;

    /// <summary>
    /// Liste des membres du groupe.
    /// </summary>
    public List<User> Members { get; set; } = new();
}
