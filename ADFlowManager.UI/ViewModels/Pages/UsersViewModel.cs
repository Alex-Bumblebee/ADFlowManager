using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// Wrapper autour de User pour ajouter IsSelected (sélection DataGrid).
/// </summary>
public partial class UserViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private User _user;

    /// <summary>
    /// Événement déclenché quand IsSelected change, pour mettre à jour le compteur parent.
    /// </summary>
    public event Action? SelectionChanged;

    public UserViewModel(User user)
    {
        _user = user;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }
}

/// <summary>
/// ViewModel de la page Utilisateurs.
/// Gère la liste, la recherche, la sélection et les actions sur les utilisateurs AD.
/// </summary>
public partial class UsersViewModel : ObservableObject
{
    private readonly IActiveDirectoryService _adService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<UsersViewModel> _logger;
    private readonly ILocalizationService _localization;

    private List<UserViewModel> _allUsers = [];

    [ObservableProperty]
    private ObservableCollection<UserViewModel> _users = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(HasSingleSelection))]
    private UserViewModel? _selectedUser;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingText = "";

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _canCompareUsers;

    [ObservableProperty]
    private bool _hasMultipleChecked;

    public bool HasSelection => SelectedUser is not null;
    public bool HasSingleSelection => SelectedUser is not null;

    public UsersViewModel(
        IActiveDirectoryService adService,
        ICacheService cacheService,
        ILogger<UsersViewModel> logger,
        ILocalizationService localization)
    {
        _adService = adService;
        _cacheService = cacheService;
        _logger = logger;
        _localization = localization;

        _ = LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            IsLoading = true;
            LoadingText = "";
            _allUsers = [];
            Users = [];

            var users = await _adService.GetUsersAsync();
            var loaded = 0;
            const int batchSize = 25;

            var batch = new List<UserViewModel>(batchSize);

            foreach (var u in users)
            {
                var vm = new UserViewModel(u);
                vm.SelectionChanged += () => UpdateSelectedCount();
                batch.Add(vm);
                loaded++;

                if (batch.Count >= batchSize)
                {
                    var toAdd = batch.ToList();
                    batch.Clear();
                    _allUsers.AddRange(toAdd);
                    foreach (var item in toAdd)
                        Users.Add(item);
                    LoadingText = string.Format(_localization.GetString("Users_LoadingProgress"), loaded);
                    await Task.Yield();
                }
            }

            if (batch.Count > 0)
            {
                _allUsers.AddRange(batch);
                foreach (var item in batch)
                    Users.Add(item);
                LoadingText = string.Format(_localization.GetString("Users_LoadingProgress"), loaded);
            }

            ApplyFilter();

            _logger.LogInformation("Users loaded: {Count}", _allUsers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading users.");
        }
        finally
        {
            IsLoading = false;
            LoadingText = "";
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<UserViewModel> filtered = _allUsers;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim().ToLowerInvariant();
            filtered = _allUsers.Where(u =>
                u.User.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.User.UserName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.User.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        Users = new ObservableCollection<UserViewModel>(filtered);
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = Users.Count(u => u.IsSelected);
        CanCompareUsers = SelectedCount == 2;
        HasMultipleChecked = SelectedCount > 1;
    }

    /// <summary>
    /// Retourne la liste des utilisateurs cochés (checkbox).
    /// </summary>
    public List<User> GetCheckedUsers()
    {
        return Users.Where(u => u.IsSelected).Select(u => u.User).ToList();
    }

    /// <summary>
    /// Retourne les 2 utilisateurs sélectionnés (checkboxes) pour la comparaison.
    /// </summary>
    public (User user1, User user2)? GetSelectedUsersForCompare()
    {
        var selected = Users.Where(u => u.IsSelected).Take(3).ToList();
        if (selected.Count != 2) return null;
        return (selected[0].User, selected[1].User);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var user in Users)
            user.IsSelected = true;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var user in Users)
            user.IsSelected = false;
        UpdateSelectedCount();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _logger.LogInformation("Refreshing users list.");
        SelectedUser = null;
        SearchText = "";
        await _cacheService.ClearCacheAsync();
        await LoadUsersAsync();
    }

    [RelayCommand]
    private void CopyUserName()
    {
        if (SelectedUser is null) return;
        System.Windows.Clipboard.SetText(SelectedUser.User.UserName);
        _logger.LogDebug("User name copied to clipboard.");
    }

    [RelayCommand]
    private void CopyEmail()
    {
        if (SelectedUser is null) return;
        System.Windows.Clipboard.SetText(SelectedUser.User.Email);
        _logger.LogDebug("User email copied to clipboard.");
    }

    [RelayCommand]
    private void CopyDn()
    {
        if (SelectedUser is null) return;
        System.Windows.Clipboard.SetText(SelectedUser.User.DistinguishedName);
        _logger.LogDebug("User DN copied to clipboard.");
    }

    /// <summary>
    /// Désactive un seul utilisateur dans AD (avec refresh UI).
    /// </summary>
    public async Task DisableUserAsync(User user)
    {
        try
        {
            await _adService.SetUserEnabledAsync(user.UserName, false);
            user.IsEnabled = false;
            await _cacheService.ClearCacheAsync();
            _logger.LogInformation("User account disabled: {UserName}", user.UserName);

            // Refresh UI
            OnPropertyChanged(nameof(SelectedUser));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while disabling user {UserName}", user.UserName);
        }
    }

    /// <summary>
    /// Désactive tous les utilisateurs cochés, un par un.
    /// </summary>
    public async Task BulkDisableAsync()
    {
        var checkedUsers = GetCheckedUsers();
        if (checkedUsers.Count == 0) return;

        _logger.LogInformation("Bulk disable requested for {Count} users", checkedUsers.Count);

        foreach (var user in checkedUsers)
        {
            if (!user.IsEnabled) continue;
            await DisableUserAsync(user);
        }

        // Force refresh de la liste pour mettre à jour les badges de statut
        OnPropertyChanged(nameof(Users));
    }
}
