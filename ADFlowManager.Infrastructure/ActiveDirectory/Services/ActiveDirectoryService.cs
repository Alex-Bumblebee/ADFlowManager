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
    private PrincipalContext? _context;
    private string? _connectedDomain;
    private string? _connectedUser;
    private string? _adminUser;
    private string? _adminPassword;

    /// <summary>
    /// Constructeur avec injection du logger et du service de cache.
    /// </summary>
    public ActiveDirectoryService(ILogger<ActiveDirectoryService> logger, ICacheService cacheService, IAuditService auditService)
    {
        _logger = logger;
        _cacheService = cacheService;
        _auditService = auditService;
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
            _logger.LogInformation("Tentative de connexion à Active Directory...");
            _logger.LogInformation("Domaine: {Domain}, Utilisateur: {Username}", domain, username);

            // Déconnexion si déjà connecté
            if (_context != null)
            {
                _logger.LogWarning("Une connexion existante a été détectée. Déconnexion...");
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
                _adminUser = username;
                _adminPassword = password;
                _logger.LogInformation("✅ Connexion à Active Directory réussie !");
                _logger.LogInformation("Domaine connecté: {Domain}", _connectedDomain);
                return true;
            }
            else
            {
                _logger.LogWarning("❌ Échec de connexion : Credentials invalides");
                await DisconnectAsync();
                return false;
            }
        }
        catch (PrincipalServerDownException ex)
        {
            _logger.LogError(ex, "❌ Erreur : Le serveur de domaine '{Domain}' est inaccessible", domain);
            await DisconnectAsync();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur inattendue lors de la connexion à Active Directory");
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
                _logger.LogInformation("Déconnexion d'Active Directory...");
                _context.Dispose();
                _context = null;
                _connectedDomain = null;
                _connectedUser = null;
                _logger.LogInformation("✅ Déconnexion réussie");
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
                _logger.LogInformation("⚡ Users chargés depuis cache : {Count}", cachedUsers.Count);
                return cachedUsers;
            }
        }

        if (!IsConnected)
        {
            _logger.LogWarning("❌ Impossible de récupérer les utilisateurs : Non connecté à AD");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        try
        {
            _logger.LogInformation("📋 Récupération des utilisateurs AD (depuis serveur)...");
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                _logger.LogInformation("Filtre de recherche: {SearchFilter}", searchFilter);
            }

            var users = new List<User>();

            await Task.Run(() =>
            {
                // Créer un principal de recherche
                var userPrincipal = new UserPrincipal(_context!)
                {
                    // Appliquer le filtre si fourni
                    Name = string.IsNullOrWhiteSpace(searchFilter) ? "*" : $"*{searchFilter}*"
                };

                using var searcher = new PrincipalSearcher(userPrincipal);
                var results = searcher.FindAll();

                foreach (Principal principal in results)
                {
                    if (principal is UserPrincipal userPrin)
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

                            // Charger les groupes de l'utilisateur
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

                            users.Add(user);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Erreur lors de la lecture de l'utilisateur {UserName}",
                                userPrin.SamAccountName ?? "Unknown");
                        }
                    }
                }
            });

            _logger.LogInformation("✅ {Count} utilisateur(s) récupéré(s)", users.Count);

            // Mettre en cache si requête sans filtre
            if (string.IsNullOrWhiteSpace(searchFilter))
            {
                await _cacheService.CacheUsersAsync(users);
            }

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur lors de la récupération des utilisateurs");
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
                _logger.LogInformation("⚡ Groups chargés depuis cache : {Count}", cachedGroups.Count);
                return cachedGroups;
            }
        }

        if (!IsConnected)
        {
            _logger.LogWarning("❌ Impossible de récupérer les groupes : Non connecté à AD");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        try
        {
            _logger.LogInformation("📋 Récupération des groupes AD (depuis serveur)...");
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                _logger.LogInformation("Filtre de recherche: {SearchFilter}", searchFilter);
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

            _logger.LogInformation("✅ {Count} groupe(s) récupéré(s)", groups.Count);

            // Mettre en cache si requête sans filtre
            if (string.IsNullOrWhiteSpace(searchFilter))
            {
                await _cacheService.CacheGroupsAsync(groups);
            }

            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur lors de la récupération des groupes");
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
            _logger.LogWarning("❌ Impossible de récupérer l'utilisateur : Non connecté à AD");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        try
        {
            _logger.LogInformation("Recherche de l'utilisateur: {Username}", username);

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

                    _logger.LogInformation("Utilisateur trouve: {DisplayName} ({GroupCount} groupes)", user.DisplayName, user.Groups.Count);
                }
                else
                {
                    _logger.LogWarning("⚠️ Utilisateur '{Username}' non trouvé", username);
                }
            });

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur lors de la recherche de l'utilisateur {Username}", username);
            throw;
        }
    }

    /// <summary>
    /// Stocke les credentials admin pour les opérations ultérieures.
    /// </summary>
    public void StoreCredentials(string domain, string username, string password)
    {
        _connectedDomain = domain;
        _adminUser = username;
        _adminPassword = password;
        _logger.LogInformation("Credentials admin stockés pour {Domain}/{Username}", domain, username);
    }

    /// <summary>
    /// Crée un nouvel utilisateur dans Active Directory.
    /// </summary>
    public async Task<User> CreateUserAsync(User user, string ouPath, string password,
        bool mustChangePassword = true, bool passwordNeverExpires = false, bool accountDisabled = false)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        if (string.IsNullOrWhiteSpace(_adminUser) || string.IsNullOrWhiteSpace(_adminPassword))
            throw new InvalidOperationException("Credentials admin non disponibles.");

        try
        {
            _logger.LogInformation("➕ Création utilisateur : {SAM} dans {OU}", user.UserName, ouPath);

            await Task.Run(() =>
            {
                // Contexte pointant vers l'OU cible
                using var ouContext = new PrincipalContext(
                    ContextType.Domain,
                    _connectedDomain!,
                    ouPath,
                    _adminUser,
                    _adminPassword);

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
                    PasswordNeverExpires = passwordNeverExpires
                };

                // Définir le mot de passe
                newUser.SetPassword(password);

                // Forcer changement au prochain logon
                if (mustChangePassword)
                    newUser.ExpirePasswordNow();

                // Sauvegarder dans AD
                newUser.Save();

                _logger.LogInformation("✅ Utilisateur créé dans AD : {SAM}", user.UserName);

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
                    _logger.LogInformation("✅ Propriétés étendues définies");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Erreur définition propriétés étendues (utilisateur créé quand même)");
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
            _logger.LogError(ex, "❌ Erreur création utilisateur {SAM}", user.UserName);
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
            _logger.LogInformation("👥 Chargement des membres du groupe {Group}", groupName);
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

            _logger.LogInformation("✅ {Count} membre(s) chargé(s) pour {Group}", members.Count, groupName);
            return members;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur chargement membres du groupe {Group}", groupName);
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
            _logger.LogInformation("👥 Ajout {User} au groupe {Group}", samAccountName, groupName);

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

                _logger.LogInformation("✅ {User} ajouté au groupe {Group}", samAccountName, groupName);
            });

            await _auditService.LogAsync(AuditActionType.AddUserToGroup, AuditEntityType.User,
                samAccountName, samAccountName, new { Group = groupName });
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.AddUserToGroup, AuditEntityType.User,
                samAccountName, samAccountName, new { Group = groupName }, false, ex.Message);
            _logger.LogError(ex, "❌ Erreur ajout {User} au groupe {Group}", samAccountName, groupName);
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
            _logger.LogInformation("👥 Retrait {User} du groupe {Group}", samAccountName, groupName);

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

                _logger.LogInformation("✅ {User} retiré du groupe {Group}", samAccountName, groupName);
            });

            await _auditService.LogAsync(AuditActionType.RemoveUserFromGroup, AuditEntityType.User,
                samAccountName, samAccountName, new { Group = groupName });
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.RemoveUserFromGroup, AuditEntityType.User,
                samAccountName, samAccountName, new { Group = groupName }, false, ex.Message);
            _logger.LogError(ex, "❌ Erreur retrait {User} du groupe {Group}", samAccountName, groupName);
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
            _logger.LogInformation("✏️ Mise à jour utilisateur : {SAM}", user.UserName);

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
                    _logger.LogWarning(ex, "⚠️ Erreur mise à jour propriétés étendues de {SAM}", user.UserName);
                }

                _logger.LogInformation("✅ Utilisateur mis à jour dans AD : {SAM}", user.UserName);
            });

            // Invalider le cache après modification
            await _cacheService.ClearCacheAsync();
            _logger.LogInformation("🗑️ Cache invalidé après mise à jour de {SAM}", user.UserName);

            await _auditService.LogAsync(AuditActionType.UpdateUser, AuditEntityType.User,
                user.UserName, user.DisplayName);
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.UpdateUser, AuditEntityType.User,
                user.UserName, user.DisplayName, null, false, ex.Message);
            _logger.LogError(ex, "❌ Erreur mise à jour utilisateur {SAM}", user.UserName);
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
            _logger.LogInformation("🔑 Reset mot de passe pour : {SAM}", samAccountName);

            await Task.Run(() =>
            {
                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, samAccountName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{samAccountName}' introuvable dans AD.");

                userPrincipal.SetPassword(newPassword);

                if (mustChangeAtNextLogon)
                    userPrincipal.ExpirePasswordNow();

                userPrincipal.Save();

                _logger.LogInformation("✅ Mot de passe réinitialisé pour {SAM}", samAccountName);
            });

            await _auditService.LogAsync(AuditActionType.ResetPassword, AuditEntityType.User,
                samAccountName, samAccountName, new { MustChangeAtNextLogon = mustChangeAtNextLogon });
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(AuditActionType.ResetPassword, AuditEntityType.User,
                samAccountName, samAccountName, null, false, ex.Message);
            _logger.LogError(ex, "❌ Erreur reset mot de passe {SAM}", samAccountName);
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
            _logger.LogInformation("🔄 {Action} du compte : {SAM}", action, samAccountName);

            await Task.Run(() =>
            {
                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, samAccountName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{samAccountName}' introuvable dans AD.");

                userPrincipal.Enabled = enabled;
                userPrincipal.Save();

                _logger.LogInformation("✅ Compte {SAM} : {Action}", samAccountName, action);
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
            _logger.LogError(ex, "❌ Erreur activation/désactivation {SAM}", samAccountName);
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

        try
        {
            _logger.LogInformation("📦 Déplacement {User} vers {OU}", samAccountName, targetOuPath);

            await Task.Run(() =>
            {
                using var userPrincipal = UserPrincipal.FindByIdentity(_context!, samAccountName);
                if (userPrincipal == null)
                    throw new InvalidOperationException($"Utilisateur '{samAccountName}' introuvable dans AD.");

                var userDe = (DirectoryEntry)userPrincipal.GetUnderlyingObject();

                using var targetOu = new DirectoryEntry(
                    $"LDAP://{_connectedDomain}/{targetOuPath}",
                    _adminUser,
                    _adminPassword);

                userDe.MoveTo(targetOu);

                _logger.LogInformation("✅ {User} déplacé vers {OU}", samAccountName, targetOuPath);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur déplacement {User} vers {OU}", samAccountName, targetOuPath);
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

        if (string.IsNullOrWhiteSpace(_adminUser) || string.IsNullOrWhiteSpace(_adminPassword))
            throw new InvalidOperationException("Credentials admin non disponibles.");

        try
        {
            _logger.LogInformation("➕ Création groupe : {Group} dans {OU}", groupName, ouPath);

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
                    _adminUser,
                    _adminPassword);

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

                _logger.LogInformation("✅ Groupe créé dans AD : {Group}", groupName);
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
            _logger.LogError(ex, "❌ Erreur création groupe {Group}", groupName);
            throw;
        }
    }

    /// <summary>
    /// Récupère la liste des OUs du domaine via DirectorySearcher.
    /// </summary>
    public async Task<List<OrganizationalUnitInfo>> GetOrganizationalUnitsAsync()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Non connecté à Active Directory.");

        try
        {
            _logger.LogInformation("📂 Chargement des OUs depuis AD...");

            var ous = new List<OrganizationalUnitInfo>();

            await Task.Run(() =>
            {
                var rootDN = _context!.ConnectedServer;
                var domainParts = _connectedDomain!.Split('.');
                var domainDN = string.Join(",", domainParts.Select(p => $"DC={p}"));

                using var rootEntry = new DirectoryEntry(
                    $"LDAP://{_connectedDomain}/{domainDN}",
                    _adminUser,
                    _adminPassword);

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

            _logger.LogInformation("✅ {Count} OU(s) trouvée(s)", ous.Count);
            return ous;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur chargement OUs");
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
