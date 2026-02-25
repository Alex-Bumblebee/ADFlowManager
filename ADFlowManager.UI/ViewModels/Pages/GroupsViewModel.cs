using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// Wrapper autour de Group pour ajouter IsSelected (sélection multiple DataGrid).
/// </summary>
public partial class GroupViewModel : ObservableObject
{
    private readonly Group _group;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<User> _members = [];

    [ObservableProperty]
    private int _memberCount;

    [ObservableProperty]
    private bool _membersLoaded;

    public GroupViewModel(Group group)
    {
        _group = group;
        _memberCount = group.Members.Count;
        _members = new ObservableCollection<User>(group.Members);
    }

    public Group Group => _group;
    public string GroupName => _group.GroupName;
    public string Description => _group.Description;
    public string DistinguishedName => _group.DistinguishedName;

    /// <summary>
    /// Met à jour la liste des membres du groupe.
    /// </summary>
    public void SetMembers(List<User> members)
    {
        Members = new ObservableCollection<User>(members);
        MemberCount = members.Count;
        MembersLoaded = true;
    }

    /// <summary>
    /// Événement déclenché quand IsSelected change.
    /// </summary>
    public event Action? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }
}

/// <summary>
/// ViewModel de la page Groupes.
/// Gère la liste, la recherche, la sélection multiple et les actions sur les groupes AD.
/// </summary>
public partial class GroupsViewModel : ObservableObject
{
    private readonly IActiveDirectoryService _adService;
    private readonly ICacheService _cacheService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<GroupsViewModel> _logger;
    private readonly ILocalizationService _localization;

    private List<GroupViewModel> _allGroups = [];

    [ObservableProperty]
    private ObservableCollection<GroupViewModel> _filteredGroups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private GroupViewModel? _selectedGroup;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _filteredGroupsCount;

    [ObservableProperty]
    private int _selectedGroupsCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasMultipleChecked;

    // === Création de groupe ===
    [ObservableProperty]
    private bool _isCreateGroupVisible;

    [ObservableProperty]
    private string _newGroupName = "";

    [ObservableProperty]
    private string _newGroupDescription = "";

    [ObservableProperty]
    private string _newGroupOU = "";

    [ObservableProperty]
    private int _newGroupTypeIndex; // 0 = Sécurité, 1 = Distribution

    [ObservableProperty]
    private int _newGroupScopeIndex; // 0 = Global, 1 = DomainLocal, 2 = Universal

    public List<string> GroupTypes { get; } = ["Sécurité", "Distribution"];
    public List<string> GroupScopes { get; } = ["Global", "Domain Local", "Universal"];

    public bool HasSelection => SelectedGroup is not null;

    public GroupsViewModel(
        IActiveDirectoryService adService,
        ICacheService cacheService,
        ISettingsService settingsService,
        ILogger<GroupsViewModel> logger,
        ILocalizationService localization)
    {
        _adService = adService;
        _cacheService = cacheService;
        _settingsService = settingsService;
        _logger = logger;
        _localization = localization;

        _ = LoadGroupsAsync();
    }

    private async Task LoadGroupsAsync()
    {
        try
        {
            IsLoading = true;

            var groups = await _adService.GetGroupsAsync();

            _allGroups = groups.Select(g =>
            {
                var gvm = new GroupViewModel(g);
                gvm.SelectionChanged += () => UpdateSelectedCount();
                return gvm;
            }).ToList();

            ApplyFilter();

            _logger.LogInformation("Groups loaded: {Count}", _allGroups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading groups.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Charge les membres d'un groupe à la demande quand on le sélectionne.
    /// </summary>
    partial void OnSelectedGroupChanged(GroupViewModel? value)
    {
        if (value is not null && !value.MembersLoaded)
        {
            _ = LoadGroupMembersAsync(value);
        }
    }

    private async Task LoadGroupMembersAsync(GroupViewModel groupVm)
    {
        try
        {
            var members = await _adService.GetGroupMembersAsync(groupVm.GroupName);
            groupVm.SetMembers(members);
            _logger.LogInformation("Group members loaded for {Group}: {Count}", groupVm.GroupName, members.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while loading members for group {Group}", groupVm.GroupName);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<GroupViewModel> filtered = _allGroups;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim().ToLowerInvariant();
            filtered = _allGroups.Where(g =>
                g.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                g.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        FilteredGroups = new ObservableCollection<GroupViewModel>(filtered);
        FilteredGroupsCount = FilteredGroups.Count;
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedGroupsCount = _allGroups.Count(g => g.IsSelected);
        HasMultipleChecked = SelectedGroupsCount > 1;
    }

    /// <summary>
    /// Retourne la liste des groupes cochés (checkbox).
    /// </summary>
    public List<GroupViewModel> GetCheckedGroups()
    {
        return FilteredGroups.Where(g => g.IsSelected).ToList();
    }

    /// <summary>
    /// Ajoute des utilisateurs à un ou plusieurs groupes dans AD.
    /// </summary>
    public async Task AddMembersToGroupsAsync(List<string> groupNames, List<User> usersToAdd)
    {
        _logger.LogInformation("Adding {UserCount} user(s) to {GroupCount} group(s)",
            usersToAdd.Count, groupNames.Count);

        foreach (var groupName in groupNames)
        {
            foreach (var user in usersToAdd)
            {
                try
                {
                    await _adService.AddUserToGroupAsync(user.UserName, groupName);
                    _logger.LogInformation("User added to group: {User}/{Group}", user.UserName, groupName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add user to group: {User}/{Group}", user.UserName, groupName);
                }
            }

            // Refresh members for this group if it's in our list
            var groupVm = _allGroups.FirstOrDefault(g => g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            if (groupVm is not null)
            {
                groupVm.MembersLoaded = false;
                await LoadGroupMembersAsync(groupVm);
            }
        }

        await _cacheService.ClearCacheAsync();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var group in FilteredGroups)
            group.IsSelected = true;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var group in FilteredGroups)
            group.IsSelected = false;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger.LogInformation("Refreshing groups list.");
        SelectedGroup = null;
        SearchText = "";
        await LoadGroupsAsync();
    }

    [RelayCommand]
    private void CopyGroupName()
    {
        if (SelectedGroup is null) return;
        System.Windows.Clipboard.SetText(SelectedGroup.GroupName);
        _logger.LogDebug("Group name copied to clipboard.");
    }

    [RelayCommand]
    private void CopyDn()
    {
        if (SelectedGroup is null) return;
        System.Windows.Clipboard.SetText(SelectedGroup.DistinguishedName);
        _logger.LogDebug("Group DN copied to clipboard.");
    }

    /// <summary>
    /// Retire un utilisateur d'un groupe dans AD et rafraîchit la liste des membres.
    /// </summary>
    public async Task RemoveMemberFromGroupAsync(string groupName, User user)
    {
        try
        {
            await _adService.RemoveUserFromGroupAsync(user.UserName, groupName);
            _logger.LogInformation("User removed from group: {User}/{Group}", user.UserName, groupName);

            // Refresh
            var groupVm = _allGroups.FirstOrDefault(g => g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            if (groupVm is not null)
            {
                groupVm.Members.Remove(user);
                groupVm.MemberCount = groupVm.Members.Count;
            }

            await _cacheService.ClearCacheAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove user from group: {User}/{Group}", user.UserName, groupName);
        }
    }

    // === Création de groupe ===

    [RelayCommand]
    private void ShowCreateGroup()
    {
        // Pré-remplir l'OU par défaut depuis les paramètres
        NewGroupOU = _settingsService.CurrentSettings.ActiveDirectory.DefaultGroupOU;
        NewGroupName = "";
        NewGroupDescription = "";
        NewGroupTypeIndex = 0;
        NewGroupScopeIndex = 0;
        IsCreateGroupVisible = true;
    }

    [RelayCommand]
    private void HideCreateGroup()
    {
        IsCreateGroupVisible = false;
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            MessageBox.Show(_localization.GetString("Groups_NameRequired"), _localization.GetString("Common_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(NewGroupOU))
        {
            MessageBox.Show(_localization.GetString("Groups_OURequired"), _localization.GetString("Common_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            bool isSecurityGroup = NewGroupTypeIndex == 0;
            string scope = NewGroupScopeIndex switch
            {
                1 => "DomainLocal",
                2 => "Universal",
                _ => "Global"
            };

            var group = await _adService.CreateGroupAsync(
                NewGroupName.Trim(),
                NewGroupDescription.Trim(),
                NewGroupOU.Trim(),
                isSecurityGroup,
                scope);

            _logger.LogInformation("Group created: {Group}", group.GroupName);

            // Ajouter à la liste locale
            var gvm = new GroupViewModel(group);
            gvm.SelectionChanged += () => UpdateSelectedCount();
            _allGroups.Add(gvm);
            ApplyFilter();

            await _cacheService.ClearCacheAsync();

            IsCreateGroupVisible = false;

            MessageBox.Show(
                string.Format(_localization.GetString("Groups_CreateSuccess"), group.GroupName, NewGroupOU),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while creating group {Group}", NewGroupName);
            MessageBox.Show(
                string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Permet de sélectionner l'OU via le browser AD pour la création de groupe.
    /// Appelé depuis le code-behind.
    /// </summary>
    public async Task<List<OrganizationalUnitInfo>> GetAvailableOUsAsync()
    {
        try
        {
            return await _adService.GetOrganizationalUnitsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading OUs.");
            return [];
        }
    }

    /// <summary>
    /// Définit l'OU de destination pour la création de groupe.
    /// </summary>
    public void SetNewGroupOU(string ouPath)
    {
        NewGroupOU = ouPath;
    }
}
