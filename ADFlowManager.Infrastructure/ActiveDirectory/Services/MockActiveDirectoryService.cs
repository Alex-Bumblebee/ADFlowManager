using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.ActiveDirectory.Services;

/// <summary>
/// ⚠️ SERVICE MOCK - Pour développement UI uniquement.
/// Implémentation fictive d'IActiveDirectoryService qui retourne des données de test.
/// Permet de développer l'UI sans avoir besoin d'une VM Active Directory.
/// </summary>
public class MockActiveDirectoryService : IActiveDirectoryService
{
    private readonly ILogger<MockActiveDirectoryService> _logger;
    private bool _isConnected;
    private string? _connectedDomain;
    private string? _connectedUser;

    // Données fictives en mémoire
    private readonly List<User> _mockUsers;
    private readonly List<Group> _mockGroups;

    public MockActiveDirectoryService(ILogger<MockActiveDirectoryService> logger)
    {
        _logger = logger;
        _isConnected = false;

        // Initialiser les utilisateurs fictifs
        _mockUsers = GenerateMockUsers();

        // Initialiser les groupes fictifs
        _mockGroups = GenerateMockGroups();
    }

    /// <summary>
    /// Vérifie si une connexion active existe (toujours true en mode mock après connexion).
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Nom du domaine AD actuellement connecté.
    /// </summary>
    public string? ConnectedDomain => _connectedDomain;

    /// <summary>
    /// Nom de l'utilisateur actuellement connecté au domaine AD.
    /// </summary>
    public string? ConnectedUser => _connectedUser;

    /// <summary>
    /// Connecte au serveur Active Directory (mode mock - accepte n'importe quel credential).
    /// </summary>
    public async Task<bool> ConnectAsync(string domain, string username, string password)
    {
        await Task.Delay(500); // Simuler une latence réseau

        _logger.LogWarning("⚠️ MODE MOCK ACTIVÉ - Service ActiveDirectory fictif !");
        _logger.LogInformation("Connexion mock à Active Directory...");
        _logger.LogInformation("Domaine: {Domain}, Utilisateur: {Username}", domain, username);

        _isConnected = true;
        _connectedDomain = domain;
        _connectedUser = username;

        _logger.LogInformation("✅ Connexion mock réussie !");
        _logger.LogInformation("📋 {UserCount} utilisateurs et {GroupCount} groupes disponibles",
            _mockUsers.Count, _mockGroups.Count);

        return true;
    }

    /// <summary>
    /// Déconnecte du serveur Active Directory (mode mock).
    /// </summary>
    public async Task DisconnectAsync()
    {
        await Task.Delay(100); // Simuler une latence

        if (_isConnected)
        {
            _logger.LogInformation("Déconnexion mock d'Active Directory...");
            _isConnected = false;
            _connectedDomain = null;
            _connectedUser = null;
            _logger.LogInformation("✅ Déconnexion mock réussie");
        }
    }

    /// <summary>
    /// Récupère la liste des utilisateurs AD fictifs avec filtre optionnel.
    /// </summary>
    public async Task<List<User>> GetUsersAsync(string searchFilter = "")
    {
        if (!IsConnected)
        {
            _logger.LogWarning("❌ Impossible de récupérer les utilisateurs : Non connecté à AD");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        await Task.Delay(300); // Simuler une latence réseau

        _logger.LogInformation("Récupération des utilisateurs mock...");
        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            _logger.LogInformation("Filtre de recherche: {SearchFilter}", searchFilter);
        }

        // Appliquer le filtre si fourni
        var filteredUsers = string.IsNullOrWhiteSpace(searchFilter)
            ? _mockUsers
            : _mockUsers.Where(u =>
                u.UserName.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                u.DisplayName.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        _logger.LogInformation("✅ {Count} utilisateur(s) mock récupéré(s)", filteredUsers.Count);
        return filteredUsers;
    }

    /// <summary>
    /// Récupère la liste des groupes AD fictifs avec filtre optionnel.
    /// </summary>
    public async Task<List<Group>> GetGroupsAsync(string searchFilter = "")
    {
        if (!IsConnected)
        {
            _logger.LogWarning("❌ Impossible de récupérer les groupes : Non connecté à AD");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        await Task.Delay(200); // Simuler une latence réseau

        _logger.LogInformation("Récupération des groupes mock...");
        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            _logger.LogInformation("Filtre de recherche: {SearchFilter}", searchFilter);
        }

        // Appliquer le filtre si fourni
        var filteredGroups = string.IsNullOrWhiteSpace(searchFilter)
            ? _mockGroups
            : _mockGroups.Where(g =>
                g.GroupName.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                g.Description.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        _logger.LogInformation("✅ {Count} groupe(s) mock récupéré(s)", filteredGroups.Count);
        return filteredGroups;
    }

    /// <summary>
    /// Récupère un utilisateur spécifique par son nom d'utilisateur (mode mock).
    /// </summary>
    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("❌ Impossible de récupérer l'utilisateur : Non connecté à AD");
            throw new InvalidOperationException("Non connecté à Active Directory. Appelez ConnectAsync() d'abord.");
        }

        await Task.Delay(150); // Simuler une latence réseau

        _logger.LogInformation("Recherche mock de l'utilisateur: {Username}", username);

        var user = _mockUsers.FirstOrDefault(u =>
            u.UserName.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user != null)
        {
            _logger.LogInformation("✅ Utilisateur mock trouvé: {DisplayName}", user.DisplayName);
        }
        else
        {
            _logger.LogWarning("⚠️ Utilisateur mock '{Username}' non trouvé", username);
        }

        return user;
    }

    /// <summary>
    /// Crée un utilisateur fictif (mock).
    /// </summary>
    public async Task<User> CreateUserAsync(User user, string ouPath, string password,
        bool mustChangePassword = true, bool passwordNeverExpires = false, bool accountDisabled = false, DateTime? accountExpirationDate = null)
    {
        await Task.Delay(500);

        _logger.LogInformation("⚠️ MOCK CreateUser : {SAM} dans {OU}", user.UserName, ouPath);

        user.DistinguishedName = $"CN={user.DisplayName},{ouPath}";
        user.IsEnabled = !accountDisabled;
        _mockUsers.Add(user);

        _logger.LogInformation("✅ MOCK Utilisateur créé : {SAM}", user.UserName);
        return user;
    }

    /// <summary>
    /// Récupère les membres d'un groupe (mock).
    /// </summary>
    public async Task<List<User>> GetGroupMembersAsync(string groupName)
    {
        await Task.Delay(200);
        _logger.LogInformation("⚠️ MOCK GetGroupMembers : {Group}", groupName);

        // Retourner les utilisateurs mock qui ont ce groupe
        var members = _mockUsers
            .Where(u => u.Groups.Any(g => g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return members;
    }

    /// <summary>
    /// Ajoute un utilisateur à un groupe (mock).
    /// </summary>
    public async Task AddUserToGroupAsync(string samAccountName, string groupName)
    {
        await Task.Delay(100);
        _logger.LogInformation("⚠️ MOCK AddUserToGroup : {User} → {Group}", samAccountName, groupName);
    }

    /// <summary>
    /// Retire un utilisateur d'un groupe (mock).
    /// </summary>
    public async Task RemoveUserFromGroupAsync(string samAccountName, string groupName)
    {
        await Task.Delay(100);
        _logger.LogInformation("⚠️ MOCK RemoveUserFromGroup : {User} ← {Group}", samAccountName, groupName);
    }

    /// <summary>
    /// Met à jour les propriétés d'un utilisateur (mock).
    /// </summary>
    public async Task UpdateUserAsync(User user)
    {
        await Task.Delay(300);
        _logger.LogInformation("⚠️ MOCK UpdateUser : {SAM}", user.UserName);
    }

    /// <summary>
    /// Réinitialise le mot de passe d'un utilisateur (mock).
    /// </summary>
    public async Task ResetPasswordAsync(string samAccountName, string newPassword, bool mustChangeAtNextLogon = true)
    {
        await Task.Delay(200);
        _logger.LogInformation("⚠️ MOCK ResetPassword : {SAM}, MustChange: {Must}", samAccountName, mustChangeAtNextLogon);
    }

    /// <summary>
    /// Active ou désactive un compte utilisateur (mock).
    /// </summary>
    public async Task SetUserEnabledAsync(string samAccountName, bool enabled)
    {
        await Task.Delay(200);
        var action = enabled ? "Activation" : "Désactivation";
        _logger.LogInformation("⚠️ MOCK {Action} : {SAM}", action, samAccountName);

        var user = _mockUsers.FirstOrDefault(u => u.UserName.Equals(samAccountName, StringComparison.OrdinalIgnoreCase));
        if (user != null) user.IsEnabled = enabled;
    }

    /// <summary>
    /// Récupère les OUs (mock).
    /// </summary>
    public async Task<List<OrganizationalUnitInfo>> GetOrganizationalUnitsAsync()
    {
        await Task.Delay(100);
        _logger.LogInformation("⚠️ MOCK GetOrganizationalUnitsAsync");

        var domain = _connectedDomain ?? "contoso.local";
        var parts = domain.Split('.');
        var dn = string.Join(",", parts.Select(p => $"DC={p}"));

        return new List<OrganizationalUnitInfo>
        {
            new() { Name = "Users", Path = $"OU=Users,{dn}" },
            new() { Name = "IT", Path = $"OU=IT,OU=Users,{dn}" },
            new() { Name = "RH", Path = $"OU=RH,OU=Users,{dn}" },
            new() { Name = "Commercial", Path = $"OU=Commercial,OU=Users,{dn}" },
            new() { Name = "Stagiaires", Path = $"OU=Stagiaires,OU=Users,{dn}" },
            new() { Name = "Disabled", Path = $"OU=Disabled,{dn}" },
            new() { Name = "Groups", Path = $"OU=Groups,{dn}" },
            new() { Name = "ServiceAccounts", Path = $"OU=ServiceAccounts,{dn}" },
        };
    }

    /// <summary>
    /// Déplace un utilisateur vers une autre OU (mock).
    /// </summary>
    public async Task MoveUserToOUAsync(string samAccountName, string targetOuPath)
    {
        await Task.Delay(100);
        _logger.LogInformation("⚠️ MOCK MoveUserToOUAsync : {User} → {OU}", samAccountName, targetOuPath);
    }

    /// <summary>
    /// Crée un nouveau groupe (mock).
    /// </summary>
    public async Task<Group> CreateGroupAsync(string groupName, string description, string ouPath,
        bool isSecurityGroup = true, string groupScope = "Global")
    {
        await Task.Delay(200);
        _logger.LogInformation("⚠️ MOCK CreateGroupAsync : {Group} dans {OU}", groupName, ouPath);

        var group = new Group
        {
            GroupName = groupName,
            Description = description,
            DistinguishedName = $"CN={groupName},{ouPath}"
        };

        _mockGroups.Add(group);
        return group;
    }

    /// <summary>
    /// Génère une liste d'utilisateurs fictifs mais réalistes.
    /// </summary>
    private static List<User> GenerateMockUsers()
    {
        return new List<User>
        {
            new User
            {
                UserName = "john.doe",
                DisplayName = "John Doe",
                FirstName = "John",
                LastName = "Doe",
                Email = "john.doe@contoso.local",
                IsEnabled = true,
                Description = "Administrateur IT",
                DistinguishedName = "CN=John Doe,OU=IT,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "jane.smith",
                DisplayName = "Jane Smith",
                FirstName = "Jane",
                LastName = "Smith",
                Email = "jane.smith@contoso.local",
                IsEnabled = true,
                Description = "Responsable RH",
                DistinguishedName = "CN=Jane Smith,OU=RH,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "bob.martin",
                DisplayName = "Bob Martin",
                FirstName = "Bob",
                LastName = "Martin",
                Email = "bob.martin@contoso.local",
                IsEnabled = true,
                Description = "Développeur Senior",
                DistinguishedName = "CN=Bob Martin,OU=IT,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "alice.johnson",
                DisplayName = "Alice Johnson",
                FirstName = "Alice",
                LastName = "Johnson",
                Email = "alice.johnson@contoso.local",
                IsEnabled = true,
                Description = "Chef de projet",
                DistinguishedName = "CN=Alice Johnson,OU=Management,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "charlie.brown",
                DisplayName = "Charlie Brown",
                FirstName = "Charlie",
                LastName = "Brown",
                Email = "charlie.brown@contoso.local",
                IsEnabled = false,
                Description = "Stagiaire (compte désactivé)",
                DistinguishedName = "CN=Charlie Brown,OU=Stagiaires,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "david.wilson",
                DisplayName = "David Wilson",
                FirstName = "David",
                LastName = "Wilson",
                Email = "david.wilson@contoso.local",
                IsEnabled = true,
                Description = "Analyste financier",
                DistinguishedName = "CN=David Wilson,OU=Finance,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "emma.davis",
                DisplayName = "Emma Davis",
                FirstName = "Emma",
                LastName = "Davis",
                Email = "emma.davis@contoso.local",
                IsEnabled = true,
                Description = "Responsable marketing",
                DistinguishedName = "CN=Emma Davis,OU=Marketing,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "frank.miller",
                DisplayName = "Frank Miller",
                FirstName = "Frank",
                LastName = "Miller",
                Email = "frank.miller@contoso.local",
                IsEnabled = true,
                Description = "Technicien support",
                DistinguishedName = "CN=Frank Miller,OU=Support,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "grace.taylor",
                DisplayName = "Grace Taylor",
                FirstName = "Grace",
                LastName = "Taylor",
                Email = "grace.taylor@contoso.local",
                IsEnabled = true,
                Description = "Designer UX/UI",
                DistinguishedName = "CN=Grace Taylor,OU=Design,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "henry.anderson",
                DisplayName = "Henry Anderson",
                FirstName = "Henry",
                LastName = "Anderson",
                Email = "henry.anderson@contoso.local",
                IsEnabled = true,
                Description = "Architecte système",
                DistinguishedName = "CN=Henry Anderson,OU=IT,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "isabel.thomas",
                DisplayName = "Isabel Thomas",
                FirstName = "Isabel",
                LastName = "Thomas",
                Email = "isabel.thomas@contoso.local",
                IsEnabled = true,
                Description = "Chef de produit",
                DistinguishedName = "CN=Isabel Thomas,OU=Product,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "jack.moore",
                DisplayName = "Jack Moore",
                FirstName = "Jack",
                LastName = "Moore",
                Email = "jack.moore@contoso.local",
                IsEnabled = true,
                Description = "Développeur Frontend",
                DistinguishedName = "CN=Jack Moore,OU=IT,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "karen.jackson",
                DisplayName = "Karen Jackson",
                FirstName = "Karen",
                LastName = "Jackson",
                Email = "karen.jackson@contoso.local",
                IsEnabled = true,
                Description = "Gestionnaire de compte",
                DistinguishedName = "CN=Karen Jackson,OU=Sales,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "leo.white",
                DisplayName = "Leo White",
                FirstName = "Leo",
                LastName = "White",
                Email = "leo.white@contoso.local",
                IsEnabled = false,
                Description = "Ex-employé (compte désactivé)",
                DistinguishedName = "CN=Leo White,OU=Former,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "maria.garcia",
                DisplayName = "Maria Garcia",
                FirstName = "Maria",
                LastName = "Garcia",
                Email = "maria.garcia@contoso.local",
                IsEnabled = true,
                Description = "Responsable qualité",
                DistinguishedName = "CN=Maria Garcia,OU=QA,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "nathan.martinez",
                DisplayName = "Nathan Martinez",
                FirstName = "Nathan",
                LastName = "Martinez",
                Email = "nathan.martinez@contoso.local",
                IsEnabled = true,
                Description = "Ingénieur réseau",
                DistinguishedName = "CN=Nathan Martinez,OU=IT,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "olivia.robinson",
                DisplayName = "Olivia Robinson",
                FirstName = "Olivia",
                LastName = "Robinson",
                Email = "olivia.robinson@contoso.local",
                IsEnabled = true,
                Description = "Spécialiste sécurité",
                DistinguishedName = "CN=Olivia Robinson,OU=Security,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "paul.clark",
                DisplayName = "Paul Clark",
                FirstName = "Paul",
                LastName = "Clark",
                Email = "paul.clark@contoso.local",
                IsEnabled = true,
                Description = "Analyste de données",
                DistinguishedName = "CN=Paul Clark,OU=Data,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "quinn.lewis",
                DisplayName = "Quinn Lewis",
                FirstName = "Quinn",
                LastName = "Lewis",
                Email = "quinn.lewis@contoso.local",
                IsEnabled = true,
                Description = "Scrum Master",
                DistinguishedName = "CN=Quinn Lewis,OU=Agile,DC=contoso,DC=local"
            },
            new User
            {
                UserName = "rachel.lee",
                DisplayName = "Rachel Lee",
                FirstName = "Rachel",
                LastName = "Lee",
                Email = "rachel.lee@contoso.local",
                IsEnabled = true,
                Description = "Directrice technique",
                DistinguishedName = "CN=Rachel Lee,OU=Management,DC=contoso,DC=local"
            }
        };
    }

    /// <summary>
    /// Génère une liste de groupes fictifs mais réalistes.
    /// </summary>
    private static List<Group> GenerateMockGroups()
    {
        return new List<Group>
        {
            new Group
            {
                GroupName = "IT-Admins",
                Description = "Administrateurs IT avec droits élevés",
                DistinguishedName = "CN=IT-Admins,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "RH-Employees",
                Description = "Employés du département Ressources Humaines",
                DistinguishedName = "CN=RH-Employees,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "Marketing-Team",
                Description = "Équipe marketing et communication",
                DistinguishedName = "CN=Marketing-Team,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "Finance-Users",
                Description = "Utilisateurs du département Finance",
                DistinguishedName = "CN=Finance-Users,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "Developers",
                Description = "Équipe de développement logiciel",
                DistinguishedName = "CN=Developers,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "Support-Team",
                Description = "Équipe support technique",
                DistinguishedName = "CN=Support-Team,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "Management",
                Description = "Direction et management",
                DistinguishedName = "CN=Management,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "Sales-Team",
                Description = "Équipe commerciale",
                DistinguishedName = "CN=Sales-Team,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "All-Users",
                Description = "Tous les utilisateurs de l'entreprise",
                DistinguishedName = "CN=All-Users,OU=Groups,DC=contoso,DC=local"
            },
            new Group
            {
                GroupName = "Remote-Workers",
                Description = "Utilisateurs en télétravail",
                DistinguishedName = "CN=Remote-Workers,OU=Groups,DC=contoso,DC=local"
            }
        };
    }
}
