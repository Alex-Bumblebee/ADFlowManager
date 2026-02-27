using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.ActiveDirectory.Services;

/// <summary>
/// Implémentation du service Active Directory utilisant System.DirectoryServices.AccountManagement.
/// Gère la connexion, la récupération et la gestion des utilisateurs/groupes AD.
/// </summary>
public class ActiveDirectoryService : IActiveDirectoryService, IDisposable
{
    private readonly ILogger<ActiveDirectoryService> _logger;
    private readonly ICacheService _cacheService;
    private readonly IAuditService _auditService;
    private readonly ISettingsService _settingsService;
    private readonly ICredentialService _credentialService;
    private PrincipalContext? _context;
    private string? _connectedDomain;
    private string? _connectedUser;

    /// <summary>
    /// Constructeur avec injection du logger et du service de cache.
    /// </summary>
    public ActiveDirectoryService(ILogger<ActiveDirectoryService> logger, ICacheService cacheService, IAuditService auditService, ISettingsService settingsService, ICredentialService credentialService)
    {
        _logger = logger;
        _cacheService = cacheService;
        _auditService = auditService;
        _settingsService = settingsService;
        _credentialService = credentialService;
    }

    /// <summary>
    /// Vérifie si une connexion active existe avec le serveur AD.
    /// </summary>
    public bool IsConnected => _context != null;

    /// <summary>
    /// Nom du domaine AD actuellement connecté.
    /// </summary>
    public string? ConnectedDomain => _connectedDomain;

    /// <summary>
    /// Nom de l'utilisateur actuellement connecté au domaine AD.
    /// </summary>
    public string? ConnectedUser => _connectedUser;

    /// <summary>
    /// Connecte au serveur Active Directory avec les credentials fournis.
    /// </summary>
    public async Task<bool> ConnectAsync(string domain, string username, string password)
    {
        try
        {
            _logger.LogInformation("Tentative de connexion à Active Directory.");
            _logger.LogDebug("Connexion AD demandée pour le domaine {Domain}.", domain);

            // Déconnexion si déjà connecté
            if (_context != null)
            {
                _logger.LogWarning("Connexion AD existante détectée. Déconnexion préalable.");
                await DisconnectAsync();
            }

            // Création du contexte AD (opération synchrone)
            await Task.Run(() =>
            {
                _context = new PrincipalContext(
                    ContextType.Domain,
                    domain,
                    username,
                    password);
            });

            // Validation des credentials
            var isValid = await Task.Run(() => _context.ValidateCredentials(username, password));

            if (isValid)
            {
                _connectedDomain = domain;
                _connectedUser = username;
                
                // Sauvegarde des credentials en session uniquement
                _credentialService.SaveSessionCredentials(domain, username, password);

                _logger.LogInformation("Connexion à Active Directory réussie.");
                _logger.LogDebug("Domaine connecté: {Domain}", _connectedDomain);
                return true;
            }
            else
            {
                _logger.LogWarning("Échec de connexion Active Directory: identifiants invalides.");
                await DisconnectAsync();
                return false;
            }
        }
        catch (PrincipalServerDownException ex)
        {
            _logger.LogError(ex, "Serveur de domaine inaccessible.");
            await DisconnectAsync();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur inattendue lors de la connexion à Active Directory.");
            await DisconnectAsync();
            return false;
        }
    }

    /// <summary>
    /// Déconnecte du serveur Active Directory et libère les ressources.
    /// </summary>
    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            if (_context != null)
            {
                _logger.LogInformation("Déconnexion d'Active Directory.");
                _context.Dispose();
                _context = null;
                _connectedDomain = null;
                _connectedUser = null;
                
                _credentialService.DeleteSessionCredentials();
                
                _logger.LogInformation("Déconnexion Active Directory terminée.");
            }
        });
    }

    /// <summary>
    /// Récupère la liste des utilisateurs AD avec filtre optionnel.
    /// </summary>
    public async Task<List<User>> GetUsersAsync(string searchFilter = "")
    {
        // Cache uniquement pour les requêtes sans filtre (liste complète)
        if (string.IsNullOrWhiteSpace(searchFilter))
        {
            var cachedUsers = await _cacheService.GetCachedUsersAsync();
            if (cachedUsers != null)
            {
                _logger.LogDebug("Utilisateurs chargés depuis le cache: {Count}", cachedUsers.Count);
                return ApplyOUFilter(cachedUsers);
            }
        }

        if (!IsConnected)
        {
            _logger.LogWarning("Impossible de récupérer les utilisateurs: non connecté à AD.");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        try
        {
            _logger.LogInformation("Récupération des utilisateurs AD.");
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                _logger.LogDebug("Récupération utilisateurs avec filtre actif.");
            }

            var users = new List<User>();
            var includedOUs = _settingsService.CurrentSettings.ActiveDirectory.IncludedUserOUs
                .Where(o => !string.IsNullOrWhiteSpace(o)).ToList();

            await Task.Run(() =>
            {
                if (includedOUs.Count > 0 && string.IsNullOrWhiteSpace(searchFilter))
                {
                    // Scan ciblé : un DirectorySearcher par OU incluse
                    _logger.LogDebug("Scan ciblé sur {Count} OU(s) incluse(s).", includedOUs.Count);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var ouDn in includedOUs)
                    {
                        try
                        {
                            var (_, sessionUser, sessionPass) = _credentialService.LoadSessionCredentials();
                            if (string.IsNullOrWhiteSpace(sessionUser) || string.IsNullOrWhiteSpace(sessionPass))
                                throw new InvalidOperationException("Credentials admin non disponibles.");

                            using var ouContext = new PrincipalContext(ContextType.Domain, _context!.Name, ouDn,
                                sessionUser, sessionPass);
                            var userPrincipal = new UserPrincipal(ouContext) { Name = "*" };
                            using var searcher = new PrincipalSearcher(userPrincipal);
                            var results = searcher.FindAll();

                            foreach (Principal principal in results)
                            {
                                if (principal is UserPrincipal userPrin)
                                {
                                    var dn = userPrin.DistinguishedName ?? string.Empty;
                                    if (!seen.Add(dn)) continue;
                                    var user = MapUserPrincipal(userPrin);
                                    if (user != null) users.Add(user);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Erreur lors du scan d'une OU incluse.");
                        }
                    }
                }
                else
                {
                    // Scan global (comportement original)
                    var userPrincipal = new UserPrincipal(_context!)
                    {
                        Name = string.IsNullOrWhiteSpace(searchFilter) ? "*" : $"*{searchFilter}*"
                    };

                    using var searcher = new PrincipalSearcher(userPrincipal);
                    var results = searcher.FindAll();

                    foreach (Principal principal in results)
                    {
                        if (principal is UserPrincipal userPrin)
                        {
                            var user = MapUserPrincipal(userPrin);
                            if (user != null) users.Add(user);
                        }
                    }
                }
            });

            _logger.LogInformation("{Count} utilisateur(s) récupéré(s) depuis AD.", users.Count);

            // Appliquer le filtrage OU si configuré
            users = ApplyOUFilter(users);

            _logger.LogDebug("{Count} utilisateur(s) après filtrage OU.", users.Count);

            // Mettre en cache si requête sans filtre
            if (string.IsNullOrWhiteSpace(searchFilter))
            {
                await _cacheService.CacheUsersAsync(users);
            }

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des utilisateurs.");
            throw;
        }
    }

    /// <summary>
    /// Récupère la liste des groupes AD avec filtre optionnel.
    /// </summary>
    public async Task<List<Group>> GetGroupsAsync(string searchFilter = "")
    {
        // Cache uniquement pour les requêtes sans filtre (liste complète)
        if (string.IsNullOrWhiteSpace(searchFilter))
        {
            var cachedGroups = await _cacheService.GetCachedGroupsAsync();
            if (cachedGroups != null)
            {
                _logger.LogDebug("Groupes chargés depuis le cache: {Count}", cachedGroups.Count);
                return cachedGroups;
            }
        }

        if (!IsConnected)
        {
            _logger.LogWarning("Impossible de récupérer les groupes: non connecté à AD.");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        try
        {
            _logger.LogInformation("Récupération des groupes AD.");
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                _logger.LogDebug("Récupération groupes avec filtre actif.");
            }

            var groups = new List<Group>();

            await Task.Run(() =>
            {
                // Créer un principal de recherche
                var groupPrincipal = new GroupPrincipal(_context!)
                {
                    // Appliquer le filtre si fourni
                    Name = string.IsNullOrWhiteSpace(searchFilter) ? "*" : $"*{searchFilter}*"
                };

                using var searcher = new PrincipalSearcher(groupPrincipal);
                var results = searcher.FindAll();

                foreach (Principal principal in results)
                {
                    if (principal is GroupPrincipal groupPrin)
                    {
                        try
                        {
                            var group = new Group
                            {
                                GroupName = groupPrin.SamAccountName ?? string.Empty,
                                Description = groupPrin.Description ?? string.Empty,
                                DistinguishedName = groupPrin.DistinguishedName ?? string.Empty
                            };

                            groups.Add(group);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Erreur lors de la lecture du groupe {GroupName}",
                                groupPrin.SamAccountName ?? "Unknown");
                        }
                    }
                }
            });

            _logger.LogInformation("{Count} groupe(s) récupéré(s).", groups.Count);

            // Mettre en cache si requête sans filtre
            if (string.IsNullOrWhiteSpace(searchFilter))
            {
                await _cacheService.CacheGroupsAsync(groups);
            }

            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des groupes.");
            throw;
        }
    }

    /// <summary>
    /// Récupère un utilisateur spécifique par son nom d'utilisateur.
    /// </summary>
    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Impossible de récupérer l'utilisateur: non connecté à AD.");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        try
        {
            _logger.LogDebug("Recherche d'un utilisateur AD.");

            User? user = null;

            await Task.Run(() =>
            {
                var userPrincipal = UserPrincipal.FindByIdentity(_context!, username);

                if (userPrincipal != null)
                {
                    user = new User
                    {
                        UserName = userPrincipal.SamAccountName ?? string.Empty,
                        DisplayName = userPrincipal.DisplayName ?? string.Empty,
                        Email = userPrincipal.EmailAddress ?? string.Empty,
                        FirstName = userPrincipal.GivenName ?? string.Empty,
                        LastName = userPrincipal.Surname ?? string.Empty,
                        DistinguishedName = userPrincipal.DistinguishedName ?? string.Empty,
                        IsEnabled = userPrincipal.Enabled ?? false,
                        Description = userPrincipal.Description ?? string.Empty,
                        UserPrincipalName = userPrincipal.UserPrincipalName ?? string.Empty
                    };

                    // Charger les propriétés étendues via DirectoryEntry
                    try
                    {
                        if (userPrincipal.GetUnderlyingObject() is DirectoryEntry de)
                        {
                            user.JobTitle = de.Properties["title"]?.Value?.ToString() ?? string.Empty;
                            user.Department = de.Properties["department"]?.Value?.ToString() ?? string.Empty;
                            user.Company = de.Properties["company"]?.Value?.ToString() ?? string.Empty;
                            user.Office = de.Properties["physicalDeliveryOfficeName"]?.Value?.ToString() ?? string.Empty;
                            user.Phone = de.Properties["telephoneNumber"]?.Value?.ToString() ?? string.Empty;
                            user.Mobile = de.Properties["mobile"]?.Value?.ToString() ?? string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Impossible de charger les propriétés étendues de {UserName}", user.UserName);
                    }

                    // Charger les groupes de l'utilisateur
                    try
                    {
                        using var memberOf = userPrincipal.GetGroups();
                        foreach (var grp in memberOf)
                        {
                            if (grp is GroupPrincipal gp)
                            {
                                user.Groups.Add(new Group
                                {
                                    GroupName = gp.SamAccountName ?? string.Empty,
                                    Description = gp.Description ?? string.Empty,
                                    DistinguishedName = gp.DistinguishedName ?? string.Empty
                                });
                            }
                            grp.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Impossible de charger les groupes de {UserName}", user.UserName);
                    }

                    _logger.LogDebug("Utilisateur trouvé ({GroupCount} groupes).", user.Groups.Count);
                }
                else
                {
                    _logger.LogWarning("Utilisateur non trouvé dans AD.");
                }
            });

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la recherche d'un utilisateur AD.");
            throw;
        }
    }

    public async Task<User> CreateUserAsync(User user, string ouPath, string password,
        bool mustChangePassword = true, bool passwordNeverExpires = false, bool accountDisabled = false, DateTime? accountExpirationDate = null)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        var (sessionDomain, sessionUser, sessionPass) = _credentialService.LoadSessionCredentials();
        if (string.IsNullOrWhiteSpace(sessionUser) || string.IsNullOrWhiteSpace(sessionPass))
            throw new InvalidOperationException("Credentials admin non disponibles.");

        try
        {
            _logger.LogInformation("Création d'un utilisateur AD: {SAM}", user.UserName);

            await Task.Run(() =>
            {
                // Contexte pointant vers l'OU cible
                using var ouContext = new PrincipalContext(
                    ContextType.Domain,
                    _connectedDomain!,
                    ouPath,
                    sessionUser,
                    sessionPass);

                // Créer le UserPrincipal
                using var newUser = new UserPrincipal(ouContext)
                {
                    SamAccountName = user.UserName,
                    UserPrincipalName = user.UserPrincipalName,
                    GivenName = user.FirstName,
                    Surname = user.LastName,
                    DisplayName = user.DisplayName,
                    EmailAddress = user.Email,
                    Description = user.Description,
                    VoiceTelephoneNumber = string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone,
                    Enabled = !accountDisabled,
                    PasswordNeverExpires = passwordNeverExpires,
                    AccountExpirationDate = accountExpirationDate
                };

                // Définir le mot de passe
                newUser.SetPassword(password);

                // Forcer changement au prochain logon
                if (mustChangePassword)
                    newUser.ExpirePasswordNow();

                // Sauvegarder dans AD
                newUser.Save();

                _logger.LogInformation("Utilisateur créé dans AD: {SAM}", user.UserName);

                // Propriétés étendues via DirectoryEntry (mobile, office, company, department, title)
                try
                {
                    var de = (DirectoryEntry)newUser.GetUnderlyingObject();

                    if (!string.IsNullOrWhiteSpace(user.Mobile))
                        de.Properties["mobile"].Value = user.Mobile;

                    if (!string.IsNullOrWhiteSpace(user.Office))
                        de.Properties["physicalDeliveryOfficeName"].Value = user.Office;

                    if (!string.IsNullOrWhiteSpace(user.Company))
                        de.Properties["company"].Value = user.Company;

                    if (!string.IsNullOrWhiteSpace(user.Department))
                        de.Properties["department"].Value = user.Department;

                    if (!string.IsNullOrWhiteSpace(user.JobTitle))
                        de.Properties["title"].Value = user.JobTitle;

                    de.CommitChanges();
                    _logger.LogDebug("Propriétés étendues définies pour l'utilisateur créé.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erreur lors de la définition des propriétés étendues (utilisateur déjà créé).");
                }

                // Mettre à jour le DN dans le modèle
                user.DistinguishedName = newUser.DistinguishedName ?? string.Empty;
            });

            await _auditService.LogAsync(AuditActionType.CreateUser, AuditEntityType.User,
                user.UserName, user.DisplayName,
                new { user.Department, user.JobTitle, OU = ouPath });

            return user;
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.CreateUser, AuditEntityType.User,
                user.UserName, user.DisplayName, null, false, ex.Message);
            _logger.LogError(ex, "Erreur lors de la création utilisateur {SAM}.", user.UserName);
            throw;
        }
    }

    /// <summary>
    /// Récupère les membres d'un groupe AD.
    /// </summary>
    public async Task<List<User>> GetGroupMembersAsync(string groupName)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        try
        {
            _logger.LogInformation("Chargement des membres d'un groupe AD.");
            var members = new List<User>();

            await Task.Run(() =>
            {
                using var group = GroupPrincipal.FindByIdentity(_context!, groupName);
                if (group == null)
                    throw new InvalidOperationException($"Groupe '{groupName}' introuvable dans AD.");

                using var groupMembers = group.GetMembers();
                foreach (var member in groupMembers)
                {
                    if (member is UserPrincipal userPrin)
                    {
                        members.Add(new User
                        {
                            UserName = userPrin.SamAccountName ?? string.Empty,
                            DisplayName = userPrin.DisplayName ?? string.Empty,
                            Email = userPrin.EmailAddress ?? string.Empty,
                            FirstName = userPrin.GivenName ?? string.Empty,
                            LastName = userPrin.Surname ?? string.Empty,
                            DistinguishedName = userPrin.DistinguishedName ?? string.Empty,
                            IsEnabled = userPrin.Enabled ?? false,
                            Description = userPrin.Description ?? string.Empty
                        });
                    }
                    member.Dispose();
                }
            });

            _logger.LogInformation("{Count} membre(s) chargé(s) pour le groupe demandé.", members.Count);
            return members;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement des membres du groupe demandé.");
            throw;
        }
    }

    /// <summary>
    /// Ajoute un utilisateur à un groupe AD.
    /// </summary>
    public async Task AddUserToGroupAsync(string samAccountName, string groupName)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        try
        {
            _logger.LogInformation("Ajout d'un utilisateur à un groupe AD: {User}", samAccountName);

            await Task.Run(() =>
            {
                using var group = GroupPrincipal.FindByIdentity(_context!, groupName);
                if (group == null)
                    throw new InvalidOperationException($"Groupe '{groupName}' introuvable dans AD.");

                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, samAccountName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{samAccountName}' introuvable dans AD.");

                group.Members.Add(userPrincipal);
                group.Save();

                _logger.LogInformation("Utilisateur ajouté au groupe AD: {User}", samAccountName);
            });

            await _auditService.LogAsync(AuditActionType.AddUserToGroup, AuditEntityType.User,
                samAccountName, samAccountName, new { Group = groupName });
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.AddUserToGroup, AuditEntityType.User,
                samAccountName, samAccountName, new { Group = groupName }, false, ex.Message);
            _logger.LogError(ex, "Erreur lors de l'ajout d'un utilisateur à un groupe AD: {User}", samAccountName);
            throw;
        }
    }

    /// <summary>
    /// Retire un utilisateur d'un groupe AD.
    /// </summary>
    public async Task RemoveUserFromGroupAsync(string samAccountName, string groupName)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        try
        {
            _logger.LogInformation("Retrait d'un utilisateur d'un groupe AD: {User}", samAccountName);

            await Task.Run(() =>
            {
                using var group = GroupPrincipal.FindByIdentity(_context!, groupName);
                if (group == null)
                    throw new InvalidOperationException($"Groupe '{groupName}' introuvable dans AD.");

                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, samAccountName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{samAccountName}' introuvable dans AD.");

                group.Members.Remove(userPrincipal);
                group.Save();

                _logger.LogInformation("Utilisateur retiré d'un groupe AD: {User}", samAccountName);
            });

            await _auditService.LogAsync(AuditActionType.RemoveUserFromGroup, AuditEntityType.User,
                samAccountName, samAccountName, new { Group = groupName });
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.RemoveUserFromGroup, AuditEntityType.User,
                samAccountName, samAccountName, new { Group = groupName }, false, ex.Message);
            _logger.LogError(ex, "Erreur lors du retrait d'un utilisateur d'un groupe AD: {User}", samAccountName);
            throw;
        }
    }

    /// <summary>
    /// Met à jour les propriétés d'un utilisateur existant dans AD.
    /// </summary>
    public async Task UpdateUserAsync(User user)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        try
        {
            _logger.LogInformation("Mise à jour d'un utilisateur AD: {SAM}", user.UserName);

            await Task.Run(() =>
            {
                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, user.UserName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{user.UserName}' introuvable dans AD.");

                // Propriétés standard via UserPrincipal
                userPrincipal.GivenName = user.FirstName;
                userPrincipal.Surname = user.LastName;
                userPrincipal.DisplayName = user.DisplayName;
                userPrincipal.Description = user.Description;
                userPrincipal.EmailAddress = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email;
                userPrincipal.VoiceTelephoneNumber = string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone;

                userPrincipal.Save();

                // Propriétés étendues via DirectoryEntry
                try
                {
                    var de = (DirectoryEntry)userPrincipal.GetUnderlyingObject();

                    SetDirectoryProperty(de, "mobile", user.Mobile);
                    SetDirectoryProperty(de, "physicalDeliveryOfficeName", user.Office);
                    SetDirectoryProperty(de, "company", user.Company);
                    SetDirectoryProperty(de, "department", user.Department);
                    SetDirectoryProperty(de, "title", user.JobTitle);

                    de.CommitChanges();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erreur lors de la mise à jour des propriétés étendues pour {SAM}", user.UserName);
                }

                _logger.LogInformation("Utilisateur mis à jour dans AD: {SAM}", user.UserName);
            });

            // Invalider le cache après modification
            await _cacheService.ClearCacheAsync();
            _logger.LogDebug("Cache invalidé après mise à jour de l'utilisateur {SAM}", user.UserName);

            await _auditService.LogAsync(AuditActionType.UpdateUser, AuditEntityType.User,
                user.UserName, user.DisplayName);
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.UpdateUser, AuditEntityType.User,
                user.UserName, user.DisplayName, null, false, ex.Message);
            _logger.LogError(ex, "Erreur lors de la mise à jour utilisateur {SAM}", user.UserName);
            throw;
        }
    }

    /// <summary>
    /// Réinitialise le mot de passe d'un utilisateur.
    /// </summary>
    public async Task ResetPasswordAsync(string samAccountName, string newPassword, bool mustChangeAtNextLogon = true)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        try
        {
            _logger.LogInformation("Réinitialisation du mot de passe pour l'utilisateur: {SAM}", samAccountName);

            await Task.Run(() =>
            {
                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, samAccountName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{samAccountName}' introuvable dans AD.");

                userPrincipal.SetPassword(newPassword);

                if (mustChangeAtNextLogon)
                    userPrincipal.ExpirePasswordNow();

                userPrincipal.Save();

                _logger.LogInformation("Mot de passe réinitialisé pour l'utilisateur: {SAM}", samAccountName);
            });

            await _auditService.LogAsync(AuditActionType.ResetPassword, AuditEntityType.User,
                samAccountName, samAccountName, new { MustChangeAtNextLogon = mustChangeAtNextLogon });
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.ResetPassword, AuditEntityType.User,
                samAccountName, samAccountName, null, false, ex.Message);
            _logger.LogError(ex, "Erreur lors de la réinitialisation du mot de passe pour {SAM}", samAccountName);
            throw;
        }
    }

    /// <summary>
    /// Active ou désactive un compte utilisateur dans AD.
    /// </summary>
    public async Task SetUserEnabledAsync(string samAccountName, bool enabled)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        try
        {
            var action = enabled ? "Activation" : "Désactivation";
            _logger.LogInformation("{Action} du compte utilisateur: {SAM}", action, samAccountName);

            await Task.Run(() =>
            {
                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, samAccountName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{samAccountName}' introuvable dans AD.");

                userPrincipal.Enabled = enabled;
                userPrincipal.Save();

                _logger.LogInformation("Compte utilisateur mis à jour: {SAM} ({Action})", samAccountName, action);
            });

            // Invalider le cache
            await _cacheService.ClearCacheAsync();

            var auditAction = enabled ? AuditActionType.EnableUser : AuditActionType.DisableUser;
            await _auditService.LogAsync(auditAction, AuditEntityType.User, samAccountName, samAccountName);
        }
        catch (Exception ex)
        {
            var auditAction = enabled ? AuditActionType.EnableUser : AuditActionType.DisableUser;
            await _auditService.LogAsync(auditAction, AuditEntityType.User,
                samAccountName, samAccountName, null, false, ex.Message);
            _logger.LogError(ex, "Erreur lors de l'activation/désactivation de {SAM}", samAccountName);
            throw;
        }
    }

    /// <summary>
    /// Définit ou efface une propriété dans un DirectoryEntry.
    /// </summary>
    private static void SetDirectoryProperty(DirectoryEntry de, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (de.Properties[propertyName].Count > 0)
                de.Properties[propertyName].Clear();
        }
        else
        {
            de.Properties[propertyName].Value = value;
        }
    }

    /// <summary>
    /// Déplace un utilisateur vers une autre OU dans AD.
    /// </summary>
    public async Task MoveUserToOUAsync(string samAccountName, string targetOuPath)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        var (sessionDomain, sessionUser, sessionPass) = _credentialService.LoadSessionCredentials();
        if (string.IsNullOrWhiteSpace(sessionUser) || string.IsNullOrWhiteSpace(sessionPass))
            throw new InvalidOperationException("Credentials admin non disponibles.");

        try
        {
            _logger.LogInformation("Déplacement d'un utilisateur vers une OU: {User}", samAccountName);

            await Task.Run(() =>
            {
                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, samAccountName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{samAccountName}' introuvable dans AD.");

                var userDe = (DirectoryEntry)userPrincipal.GetUnderlyingObject();

                using var targetOu = new DirectoryEntry(
                    $"LDAP://{_connectedDomain}/{targetOuPath}",
                    sessionUser,
                    sessionPass);

                userDe.MoveTo(targetOu);

                _logger.LogInformation("Utilisateur déplacé vers l'OU cible: {User}", samAccountName);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du déplacement d'un utilisateur vers une OU: {User}", samAccountName);
            throw;
        }
    }

    /// <summary>
    /// Crée un nouveau groupe dans Active Directory.
    /// </summary>
    public async Task<Group> CreateGroupAsync(string groupName, string description, string ouPath,
        bool isSecurityGroup = true, string groupScope = "Global")
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        var (_, sessionUser, sessionPass) = _credentialService.LoadSessionCredentials();
        if (string.IsNullOrWhiteSpace(sessionUser) || string.IsNullOrWhiteSpace(sessionPass))
            throw new InvalidOperationException("Credentials admin non disponibles.");

        try
        {
            _logger.LogInformation("Création d'un groupe AD: {Group}", groupName);

            var group = new Group
            {
                GroupName = groupName,
                Description = description
            };

            await Task.Run(() =>
            {
                using var ouContext = new PrincipalContext(
                    ContextType.Domain,
                    _connectedDomain!,
                    ouPath,
                    sessionUser,
                    sessionPass);

                var scope = groupScope.ToLower() switch
                {
                    "domainlocal" => GroupScope.Local,
                    "universal" => GroupScope.Universal,
                    _ => GroupScope.Global
                };

                using var newGroup = new GroupPrincipal(ouContext)
                {
                    SamAccountName = groupName,
                    Description = description,
                    IsSecurityGroup = isSecurityGroup,
                    GroupScope = scope
                };

                newGroup.Save();

                group.DistinguishedName = newGroup.DistinguishedName ?? string.Empty;

                _logger.LogInformation("Groupe créé dans AD: {Group}", groupName);
            });

            await _auditService.LogAsync("CreateGroup", AuditEntityType.Group,
                groupName, groupName,
                new { Description = description, OU = ouPath, SecurityGroup = isSecurityGroup, Scope = groupScope });

            return group;
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync("CreateGroup", AuditEntityType.Group,
                groupName, groupName, null, false, ex.Message);
            _logger.LogError(ex, "Erreur lors de la création du groupe {Group}", groupName);
            throw;
        }
    }

    /// <summary>
    /// Mappe un UserPrincipal vers un objet User (propriétés de base + étendues + groupes).
    /// Retourne null en cas d'erreur fatale sur cet utilisateur.
    /// </summary>
    private User? MapUserPrincipal(UserPrincipal userPrin)
    {
        try
        {
            var user = new User
            {
                UserName = userPrin.SamAccountName ?? string.Empty,
                DisplayName = userPrin.DisplayName ?? string.Empty,
                Email = userPrin.EmailAddress ?? string.Empty,
                FirstName = userPrin.GivenName ?? string.Empty,
                LastName = userPrin.Surname ?? string.Empty,
                DistinguishedName = userPrin.DistinguishedName ?? string.Empty,
                IsEnabled = userPrin.Enabled ?? false,
                Description = userPrin.Description ?? string.Empty,
                UserPrincipalName = userPrin.UserPrincipalName ?? string.Empty
            };

            // Charger les propriétés étendues via DirectoryEntry
            try
            {
                if (userPrin.GetUnderlyingObject() is DirectoryEntry de)
                {
                    user.JobTitle = de.Properties["title"]?.Value?.ToString() ?? string.Empty;
                    user.Department = de.Properties["department"]?.Value?.ToString() ?? string.Empty;
                    user.Company = de.Properties["company"]?.Value?.ToString() ?? string.Empty;
                    user.Office = de.Properties["physicalDeliveryOfficeName"]?.Value?.ToString() ?? string.Empty;
                    user.Phone = de.Properties["telephoneNumber"]?.Value?.ToString() ?? string.Empty;
                    user.Mobile = de.Properties["mobile"]?.Value?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible de charger les propriétés étendues de {UserName}", user.UserName);
            }

            // Charger les groupes uniquement si le setting est activé
            if (_settingsService.CurrentSettings.ActiveDirectory.LoadGroupsOnStartup)
            {
                try
                {
                    using var memberOf = userPrin.GetGroups();
                    foreach (var grp in memberOf)
                    {
                        if (grp is GroupPrincipal gp)
                        {
                            user.Groups.Add(new Group
                            {
                                GroupName = gp.SamAccountName ?? string.Empty,
                                Description = gp.Description ?? string.Empty,
                                DistinguishedName = gp.DistinguishedName ?? string.Empty
                            });
                        }
                        grp.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Impossible de charger les groupes de {UserName}", user.UserName);
                }
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors de la lecture de l'utilisateur {UserName}", userPrin.SamAccountName ?? "Unknown");
            return null;
        }
    }

    /// <summary>
    /// Applique le filtrage IncludedUserOUs / ExcludedUserOUs depuis les settings.
    /// Un user est inclus si son DN contient une des OUs incluses (ou si la liste est vide).
    /// Un user est exclu si son DN contient une des OUs exclues.
    /// </summary>
    private List<User> ApplyOUFilter(List<User> users)
    {
        var settings = _settingsService.CurrentSettings.ActiveDirectory;
        var included = settings.IncludedUserOUs.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
        var excluded = settings.ExcludedUserOUs.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();

        if (included.Count == 0 && excluded.Count == 0)
            return users;

        return users.Where(u =>
        {
            var dn = u.DistinguishedName;
            if (string.IsNullOrWhiteSpace(dn)) return true;

            if (excluded.Count > 0 && excluded.Any(ou => dn.Contains(ou, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (included.Count > 0 && !included.Any(ou => dn.Contains(ou, StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }).ToList();
    }

    /// <summary>
    /// Récupère la liste des OUs du domaine via DirectorySearcher.
    /// </summary>
    public async Task<List<OrganizationalUnitInfo>> GetOrganizationalUnitsAsync()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        var (sessionDomain, sessionUser, sessionPass) = _credentialService.LoadSessionCredentials();
        if (string.IsNullOrWhiteSpace(sessionUser) || string.IsNullOrWhiteSpace(sessionPass))
            throw new InvalidOperationException("Credentials admin non disponibles.");

        try
        {
            _logger.LogInformation("Chargement des OU depuis AD.");

            var ous = new List<OrganizationalUnitInfo>();

            await Task.Run(() =>
            {
                var rootDN = _context!.ConnectedServer;
                var domainParts = _connectedDomain!.Split('.');
                var domainDN = string.Join(",", domainParts.Select(p => $"DC={p}"));

                using var rootEntry = new DirectoryEntry(
                    $"LDAP://{_connectedDomain}/{domainDN}",
                    sessionUser,
                    sessionPass);

                using var searcher = new DirectorySearcher(rootEntry)
                {
                    Filter = "(objectClass=organizationalUnit)",
                    SearchScope = SearchScope.Subtree
                };
                searcher.PropertiesToLoad.Add("name");
                searcher.PropertiesToLoad.Add("distinguishedName");

                var results = searcher.FindAll();
                foreach (SearchResult result in results)
                {
                    var name = result.Properties["name"]?.Count > 0
                        ? result.Properties["name"][0]?.ToString() ?? ""
                        : "";
                    var dn = result.Properties["distinguishedName"]?.Count > 0
                        ? result.Properties["distinguishedName"][0]?.ToString() ?? ""
                        : "";

                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dn))
                    {
                        ous.Add(new OrganizationalUnitInfo { Name = name, Path = dn });
                    }
                }
            });

            _logger.LogInformation("{Count} OU(s) trouvée(s).", ous.Count);
            return ous;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement des OU.");
            throw;
        }
    }

    /// <summary>
    /// Libère les ressources utilisées par le service.
    /// </summary>
    public void Dispose()
    {
        DisconnectAsync().Wait();
        GC.SuppressFinalize(this);
    }
}
