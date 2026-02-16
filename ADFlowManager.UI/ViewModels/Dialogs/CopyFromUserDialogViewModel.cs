using System.Collections.ObjectModel;
using System.Windows;
using ADFlowManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.ViewModels.Dialogs;

/// <summary>
/// Wrapper pour afficher un user dans la liste du dialog de copie.
/// </summary>
public partial class CopyUserItemViewModel : ObservableObject
{
    public User User { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Initials => User.DisplayName.Length >= 2
        ? $"{User.DisplayName.Split(' ').FirstOrDefault()?[0]}{User.DisplayName.Split(' ').LastOrDefault()?[0]}".ToUpper()
        : "??";

    public CopyUserItemViewModel(User user) => User = user;
}

/// <summary>
/// ViewModel du dialog "Copier depuis utilisateur".
/// Permet de rechercher et sélectionner un user source pour copier ses données organisationnelles et groupes.
/// </summary>
public partial class CopyFromUserDialogViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly List<CopyUserItemViewModel> _allUserItems = [];

    // Groupes système toujours exclus de la copie
    private static readonly HashSet<string> SystemGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "Domain Users",
        "Domain Computers",
        "Domain Admins",
        "Enterprise Admins",
        "Schema Admins",
        "Administrators",
        "Account Operators",
        "Backup Operators",
        "Print Operators",
        "Server Operators",
        "Utilisateurs du domaine"
    };

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private ObservableCollection<CopyUserItemViewModel> _filteredUsers = [];

    [ObservableProperty]
    private CopyUserItemViewModel? _selectedUser;

    [ObservableProperty]
    private bool _hasSelectedUser;

    [ObservableProperty]
    private int _copyableGroupsCount;

    /// <summary>
    /// Données organisation copiées (résultat du dialog).
    /// </summary>
    public User? CopiedUserData { get; private set; }

    /// <summary>
    /// Groupes copiés (hors système).
    /// </summary>
    public List<Group>? CopiedGroups { get; private set; }

    /// <summary>
    /// Référence à la fenêtre pour fermeture.
    /// </summary>
    public Window? DialogWindow { get; set; }

    public CopyFromUserDialogViewModel(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initialise le dialog avec la liste des users AD.
    /// </summary>
    public void Initialize(List<User> users)
    {
        _allUserItems.Clear();
        foreach (var user in users)
            _allUserItems.Add(new CopyUserItemViewModel(user));

        FilteredUsers = new ObservableCollection<CopyUserItemViewModel>(_allUserItems);
        _logger.LogInformation("CopyFromUserDialog initialisé : {Count} utilisateurs", users.Count);
    }

    partial void OnSearchQueryChanged(string value) => FilterUsers();

    private void FilterUsers()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredUsers = new ObservableCollection<CopyUserItemViewModel>(_allUserItems);
            return;
        }

        var query = SearchQuery.Trim().ToLowerInvariant();
        var filtered = _allUserItems.Where(u =>
            u.User.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            u.User.UserName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            u.User.Email.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        FilteredUsers = new ObservableCollection<CopyUserItemViewModel>(filtered);
    }

    [RelayCommand]
    private void SelectUser(CopyUserItemViewModel user)
    {
        // Désélectionner l'ancien
        if (SelectedUser != null)
            SelectedUser.IsSelected = false;

        // Sélectionner le nouveau
        user.IsSelected = true;
        SelectedUser = user;
        HasSelectedUser = true;

        // Calculer groupes copiables (hors système)
        CopyableGroupsCount = user.User.Groups
            .Count(g => !SystemGroups.Contains(g.GroupName));

        _logger.LogInformation("User sélectionné pour copie : {User}, {Count} groupes copiables",
            user.User.DisplayName, CopyableGroupsCount);
    }

    [RelayCommand]
    private void Copy()
    {
        if (SelectedUser == null) return;

        _logger.LogInformation("Copie données de : {User}", SelectedUser.User.DisplayName);

        // Copier uniquement les données organisationnelles
        CopiedUserData = new User
        {
            JobTitle = SelectedUser.User.JobTitle,
            Department = SelectedUser.User.Department,
            Company = SelectedUser.User.Company,
            Office = SelectedUser.User.Office,
            Description = SelectedUser.User.Description
        };

        // Groupes copiables (hors système)
        CopiedGroups = SelectedUser.User.Groups
            .Where(g => !SystemGroups.Contains(g.GroupName))
            .ToList();

        _logger.LogInformation("Copie effectuée : {GroupCount} groupes", CopiedGroups.Count);

        // Fermer dialog avec succès
        if (DialogWindow != null)
        {
            DialogWindow.DialogResult = true;
            DialogWindow.Close();
        }
    }

    [RelayCommand]
    private void Close()
    {
        if (DialogWindow != null)
        {
            DialogWindow.DialogResult = false;
            DialogWindow.Close();
        }
    }
}
