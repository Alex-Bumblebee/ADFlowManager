using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.ViewModels.Dialogs;

/// <summary>
/// ViewModel du dialog de copie de droits entre utilisateurs.
/// Permet de rechercher un utilisateur source et copier ses groupes.
/// </summary>
public partial class CopyRightsDialogViewModel : ObservableObject
{
    private readonly IActiveDirectoryService _adService;
    private readonly ILogger<CopyRightsDialogViewModel> _logger;

    private string _targetUserName = "";
    private List<User> _allUsers = [];

    [ObservableProperty]
    private string _targetDisplayName = "";

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private ObservableCollection<User> _filteredUsers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopyRights))]
    private User? _selectedSourceUser;

    public bool CanCopyRights => SelectedSourceUser?.Groups.Count > 0;

    public CopyRightsDialogViewModel(
        IActiveDirectoryService adService,
        ILogger<CopyRightsDialogViewModel> logger)
    {
        _adService = adService;
        _logger = logger;
    }

    /// <summary>
    /// Initialise le dialog avec l'utilisateur cible et charge la liste des utilisateurs sources.
    /// </summary>
    public void Initialize(string targetUserName, string targetDisplayName)
    {
        _targetUserName = targetUserName;
        TargetDisplayName = targetDisplayName;
        _ = LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await _adService.GetUsersAsync();
            _allUsers = users.Where(u => u.UserName != _targetUserName).ToList();
            FilteredUsers = new ObservableCollection<User>(_allUsers);

            _logger.LogInformation("{Count} utilisateurs chargés pour copie droits", _allUsers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement utilisateurs pour copie droits");
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredUsers = new ObservableCollection<User>(_allUsers);
            return;
        }

        var search = value.Trim();
        var filtered = _allUsers.Where(u =>
            u.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            u.UserName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            u.Email.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        FilteredUsers = new ObservableCollection<User>(filtered);
    }
}
