using ADFlowManager.Core.Models;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Interface pour le service de gestion Active Directory.
/// Définit les opérations de base pour interagir avec AD (connexion, CRUD utilisateurs/groupes).
/// </summary>
public interface IActiveDirectoryService
{
    /// <summary>
    /// Connecte au serveur Active Directory avec les credentials fournis.
    /// </summary>
    /// <param name="domain">Nom du domaine AD (ex: "contoso.local")</param>
    /// <param name="username">Nom d'utilisateur avec droits admin AD</param>
    /// <param name="password">Mot de passe de l'utilisateur</param>
    /// <returns>True si la connexion réussit, False sinon</returns>
    Task<bool> ConnectAsync(string domain, string username, string password);

    /// <summary>
    /// Déconnecte du serveur Active Directory et libère les ressources.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Récupère la liste des utilisateurs AD avec filtre optionnel.
    /// </summary>
    /// <param name="searchFilter">Filtre de recherche (nom, email, etc.). Vide = tous les utilisateurs</param>
    /// <returns>Liste des utilisateurs trouvés</returns>
    Task<List<User>> GetUsersAsync(string searchFilter = "");

    /// <summary>
    /// Récupère la liste des groupes AD avec filtre optionnel.
    /// </summary>
    /// <param name="searchFilter">Filtre de recherche (nom du groupe). Vide = tous les groupes</param>
    /// <returns>Liste des groupes trouvés</returns>
    Task<List<Group>> GetGroupsAsync(string searchFilter = "");

    /// <summary>
    /// Récupère un utilisateur spécifique par son nom d'utilisateur.
    /// </summary>
    /// <param name="username">Nom d'utilisateur (SamAccountName)</param>
    /// <returns>L'utilisateur trouvé, ou null si inexistant</returns>
    Task<User?> GetUserByUsernameAsync(string username);

    /// <summary>
    /// Vérifie si une connexion active existe avec le serveur AD.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Nom du domaine AD actuellement connecté.
    /// </summary>
    string? ConnectedDomain { get; }

    /// <summary>
    /// Nom de l'utilisateur actuellement connecté au domaine AD.
    /// </summary>
    string? ConnectedUser { get; }

    /// <summary>
    /// Crée un nouvel utilisateur dans Active Directory.
    /// </summary>
    /// <param name="user">Modèle utilisateur avec toutes les propriétés</param>
    /// <param name="ouPath">Chemin OU destination (ex: "OU=Users,DC=contoso,DC=local")</param>
    /// <param name="password">Mot de passe initial</param>
    /// <param name="mustChangePassword">Forcer changement au prochain logon</param>
    /// <param name="passwordNeverExpires">Le mot de passe n'expire jamais</param>
    /// <param name="accountDisabled">Créer le compte désactivé</param>
    /// <param name="accountExpirationDate">Date d'expiration du compte (null = n'expire jamais)</param>
    /// <returns>L'utilisateur créé</returns>
    Task<User> CreateUserAsync(User user, string ouPath, string password,
        bool mustChangePassword = true, bool passwordNeverExpires = false, bool accountDisabled = false, DateTime? accountExpirationDate = null);

    /// <summary>
    /// Ajoute un utilisateur à un groupe AD.
    /// </summary>
    /// <param name="samAccountName">Login de l'utilisateur</param>
    /// <param name="groupName">Nom du groupe</param>
    Task AddUserToGroupAsync(string samAccountName, string groupName);

    /// <summary>
    /// Retire un utilisateur d'un groupe AD.
    /// </summary>
    /// <param name="samAccountName">Login de l'utilisateur</param>
    /// <param name="groupName">Nom du groupe</param>
    Task RemoveUserFromGroupAsync(string samAccountName, string groupName);

    /// <summary>
    /// Met à jour les propriétés d'un utilisateur existant dans AD.
    /// </summary>
    /// <param name="user">Modèle utilisateur avec les propriétés mises à jour</param>
    Task UpdateUserAsync(User user);

    /// <summary>
    /// Réinitialise le mot de passe d'un utilisateur.
    /// </summary>
    /// <param name="samAccountName">Login de l'utilisateur</param>
    /// <param name="newPassword">Nouveau mot de passe</param>
    /// <param name="mustChangeAtNextLogon">Forcer changement au prochain logon</param>
    Task ResetPasswordAsync(string samAccountName, string newPassword, bool mustChangeAtNextLogon = true);

    /// <summary>
    /// Active ou désactive un compte utilisateur dans AD.
    /// </summary>
    /// <param name="samAccountName">Login de l'utilisateur</param>
    /// <param name="enabled">True pour activer, False pour désactiver</param>
    Task SetUserEnabledAsync(string samAccountName, bool enabled);

    /// <summary>
    /// Récupère les membres d'un groupe AD.
    /// </summary>
    /// <param name="groupName">Nom du groupe (SamAccountName)</param>
    /// <returns>Liste des utilisateurs membres du groupe</returns>
    Task<List<User>> GetGroupMembersAsync(string groupName);

    /// <summary>
    /// Récupère la liste des unités organisationnelles (OU) du domaine.
    /// </summary>
    /// <returns>Liste des OUs avec nom et chemin DN</returns>
    Task<List<OrganizationalUnitInfo>> GetOrganizationalUnitsAsync();

    /// <summary>
    /// Déplace un utilisateur vers une autre OU.
    /// </summary>
    /// <param name="samAccountName">Login de l'utilisateur</param>
    /// <param name="targetOuPath">Chemin DN de l'OU cible (ex: "OU=Disabled,DC=contoso,DC=local")</param>
    Task MoveUserToOUAsync(string samAccountName, string targetOuPath);

    /// <summary>
    /// Crée un nouveau groupe dans Active Directory.
    /// </summary>
    /// <param name="groupName">Nom du groupe (SamAccountName)</param>
    /// <param name="description">Description du groupe</param>
    /// <param name="ouPath">Chemin OU destination</param>
    /// <param name="isSecurityGroup">True pour groupe de sécurité, False pour distribution</param>
    /// <param name="groupScope">Portée : Global, DomainLocal, Universal</param>
    /// <returns>Le groupe créé</returns>
    Task<Group> CreateGroupAsync(string groupName, string description, string ouPath,
        bool isSecurityGroup = true, string groupScope = "Global");
}

/// <summary>
/// Info sur une unité organisationnelle AD.
/// </summary>
public class OrganizationalUnitInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Affichage lisible : "Parent OU → OU Name" extrait du DN.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
                return Name;

            var ouParts = Path
                .Split(',')
                .Where(p => p.TrimStart().StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Trim().Substring(3))
                .ToList();

            if (ouParts.Count == 0)
                return Name;

            ouParts.Reverse();

            return string.Join(" → ", ouParts);
        }
    }
}
