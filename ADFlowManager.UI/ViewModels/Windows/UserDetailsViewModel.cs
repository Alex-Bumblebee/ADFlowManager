using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.ViewModels.Windows;

/// <summary>
/// Résultat d'une sauvegarde utilisateur avec résumé des modifications.
/// </summary>
public class SaveResultInfo
{
    public bool Success { get; set; }
    public bool HasChanges { get; set; }
    public List<string> Changes { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Entry d'historique (audit) pour un utilisateur.
/// </summary>
public class AuditEntry
{
    public DateTime Date { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}

/// <summary>
/// ViewModel de la fenêtre de détails utilisateur.
/// Mode consultation par défaut, basculer IsEditMode pour modifier.
/// </summary>
public partial class UserDetailsViewModel : ObservableObject
{
    private readonly IActiveDirectoryService _adService;
    private readonly ICacheService _cacheService;
    private readonly IAuditService _auditService;
    private readonly ILogger<UserDetailsViewModel> _logger;
    private readonly ILocalizationService _localization;

    private User? _originalUser;
    private List<string> _originalGroupNames = [];

    // === Mode ===
    [ObservableProperty]
    private bool _isEditMode;

    // === Identité ===
    [ObservableProperty]
    private string _firstName = "";

    [ObservableProperty]
    private string _lastName = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _isEnabled = true;

    // === Contact ===
    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _phone = "";

    [ObservableProperty]
    private string _mobile = "";

    // === Organisation ===
    [ObservableProperty]
    private string _jobTitle = "";

    [ObservableProperty]
    private string _department = "";

    [ObservableProperty]
    private string _company = "";

    [ObservableProperty]
    private string _office = "";

    [ObservableProperty]
    private string _distinguishedName = "";

    // === Groupes ===
    [ObservableProperty]
    private ObservableCollection<Group> _userGroups = [];

    [ObservableProperty]
    private int _groupCount;

    // === Group Search ===
    [ObservableProperty]
    private string _groupSearchText = "";

    [ObservableProperty]
    private bool _isGroupSearchOpen;

    [ObservableProperty]
    private ObservableCollection<Group> _filteredAvailableGroups = [];

    [ObservableProperty]
    private Group? _selectedAvailableGroup;

    private List<Group> _allAvailableGroups = [];

    // === Historique ===
    [ObservableProperty]
    private ObservableCollection<AuditEntry> _auditEntries = [];

    // === UI ===
    [ObservableProperty]
    private string _initials = "??";

    [ObservableProperty]
    private string _windowTitle = "";

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _saveError = "";

    /// <summary>
    /// Résultat de la dernière sauvegarde (résumé des modifications).
    /// Null si pas encore sauvegardé.
    /// </summary>
    public SaveResultInfo? LastSaveResult { get; private set; }

    public UserDetailsViewModel(
        IActiveDirectoryService adService,
        ICacheService cacheService,
        IAuditService auditService,
        ILogger<UserDetailsViewModel> logger,
        ILocalizationService localization)
    {
        _adService = adService;
        _cacheService = cacheService;
        _auditService = auditService;
        _logger = logger;
        _localization = localization;
        _windowTitle = _localization.GetString("UserDetails_WindowTitle");
    }

    // === Tab index (for opening on a specific tab) ===
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Charge les données d'un utilisateur dans le formulaire.
    /// </summary>
    public void LoadUser(User user, bool startInEditMode = false, int initialTabIndex = 0)
    {
        _originalUser = user;

        FirstName = user.FirstName;
        LastName = user.LastName;
        DisplayName = user.DisplayName;
        UserName = user.UserName;
        Description = user.Description;
        IsEnabled = user.IsEnabled;
        Email = user.Email;
        Phone = user.Phone;
        Mobile = user.Mobile;
        JobTitle = user.JobTitle;
        Department = user.Department;
        Company = user.Company;
        Office = user.Office;
        DistinguishedName = user.DistinguishedName;

        UserGroups = new ObservableCollection<Group>(user.Groups);
        GroupCount = user.Groups.Count;
        _originalGroupNames = user.Groups.Select(g => g.GroupName).ToList();

        // Initiales
        if (!string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(user.LastName))
            Initials = $"{user.FirstName[0]}{user.LastName[0]}".ToUpper();
        else if (!string.IsNullOrWhiteSpace(user.DisplayName) && user.DisplayName.Contains(' '))
        {
            var parts = user.DisplayName.Split(' ');
            Initials = $"{parts[0][0]}{parts[^1][0]}".ToUpper();
        }
        else
            Initials = user.DisplayName.Length >= 2 ? user.DisplayName[..2].ToUpper() : "??";

        WindowTitle = $"{_localization.GetString("UserDetails_WindowTitle")} — {user.DisplayName}";

        _ = LoadAuditEntriesAsync();
        IsEditMode = startInEditMode;
        SelectedTabIndex = initialTabIndex;

        _ = LoadAvailableGroupsAsync();

        _logger.LogInformation("Loading user details: {DisplayName} ({UserName}), EditMode={EditMode}, Tab={Tab}",
            user.DisplayName, user.UserName, startInEditMode, initialTabIndex);
    }

    private async Task LoadAuditEntriesAsync()
    {
        try
        {
            var logs = await _auditService.GetEntityLogsAsync(
                Core.Models.AuditEntityType.User, UserName, limit: 50);

            AuditEntries = new ObservableCollection<AuditEntry>(
                logs.Select(l => new AuditEntry
                {
                    Date = l.Timestamp,
                    Action = $"{l.ActionType} par {l.Username}",
                    Details = l.Details == "{}" ? l.Result : l.Details
                }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while loading audit history for {User}", UserName);
        }
    }

    // === Group search ===
    partial void OnGroupSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            IsGroupSearchOpen = false;
            FilteredAvailableGroups = [];
            return;
        }

        var currentGroupNames = UserGroups.Select(g => g.GroupName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = _allAvailableGroups
            .Where(g => !currentGroupNames.Contains(g.GroupName) &&
                        g.GroupName.Contains(value, StringComparison.OrdinalIgnoreCase))
            .Take(15)
            .ToList();

        FilteredAvailableGroups = new ObservableCollection<Group>(filtered);
        IsGroupSearchOpen = filtered.Count > 0;
    }

    /// <summary>
    /// Sélectionne un groupe depuis la liste de recherche et ferme le dropdown.
    /// </summary>
    public void SelectGroup(Group group)
    {
        SelectedAvailableGroup = group;
        GroupSearchText = group.GroupName;
        IsGroupSearchOpen = false;
    }

    private async Task LoadAvailableGroupsAsync()
    {
        try
        {
            var groups = await _adService.GetGroupsAsync();
            _allAvailableGroups = groups;
            _logger.LogInformation("Available groups loaded: {Count}", groups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load available groups.");
        }
    }

    // === Auto-update en mode édition ===
    partial void OnFirstNameChanged(string value) => UpdateDisplayName();
    partial void OnLastNameChanged(string value) => UpdateDisplayName();

    private void UpdateDisplayName()
    {
        if (!IsEditMode) return;
        if (!string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(LastName))
        {
            DisplayName = $"{FirstName} {LastName}";
            Initials = $"{FirstName[0]}{LastName[0]}".ToUpper();
        }
    }

    // === Commands ===
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_originalUser is null) return;

        IsSaving = true;
        SaveError = "";
        LastSaveResult = null;

        try
        {
            _logger.LogInformation("Saving user to AD: {UserName}", UserName);

            var changes = new List<string>();

            // 1. Détecter les propriétés changées et construire le résumé
            if (_originalUser.FirstName != FirstName)
                changes.Add($"{_localization.GetString("UserDetails_FirstName")} : {_originalUser.FirstName} → {FirstName}");
            if (_originalUser.LastName != LastName)
                changes.Add($"{_localization.GetString("UserDetails_LastName")} : {_originalUser.LastName} → {LastName}");
            if (_originalUser.DisplayName != DisplayName)
                changes.Add($"{_localization.GetString("UserDetails_DisplayName")} : {_originalUser.DisplayName} → {DisplayName}");
            if (_originalUser.Description != Description)
                changes.Add($"{_localization.GetString("UserDetails_Description")} : {_originalUser.Description} → {Description}");
            if (_originalUser.Email != Email)
                changes.Add($"{_localization.GetString("UserDetails_Email")} : {_originalUser.Email} → {Email}");
            if (_originalUser.Phone != Phone)
                changes.Add($"{_localization.GetString("UserDetails_Phone")} : {_originalUser.Phone} → {Phone}");
            if (_originalUser.Mobile != Mobile)
                changes.Add($"{_localization.GetString("UserDetails_Mobile")} : {_originalUser.Mobile} → {Mobile}");
            if (_originalUser.JobTitle != JobTitle)
                changes.Add($"{_localization.GetString("UserDetails_JobTitle")} : {_originalUser.JobTitle} → {JobTitle}");
            if (_originalUser.Department != Department)
                changes.Add($"{_localization.GetString("UserDetails_Department")} : {_originalUser.Department} → {Department}");
            if (_originalUser.Company != Company)
                changes.Add($"{_localization.GetString("UserDetails_Company")} : {_originalUser.Company} → {Company}");
            if (_originalUser.Office != Office)
                changes.Add($"{_localization.GetString("UserDetails_Office")} : {_originalUser.Office} → {Office}");
            if (_originalUser.IsEnabled != IsEnabled)
                changes.Add(IsEnabled ? _localization.GetString("UserDetails_AccountEnabled") : _localization.GetString("UserDetails_AccountDisabled"));

            bool propsChanged = changes.Count > 0;

            // 2. Mettre à jour le modèle
            _originalUser.FirstName = FirstName;
            _originalUser.LastName = LastName;
            _originalUser.DisplayName = DisplayName;
            _originalUser.Description = Description;
            _originalUser.Email = Email;
            _originalUser.Phone = Phone;
            _originalUser.Mobile = Mobile;
            _originalUser.JobTitle = JobTitle;
            _originalUser.Department = Department;
            _originalUser.Company = Company;
            _originalUser.Office = Office;
            _originalUser.IsEnabled = IsEnabled;

            // 3. Sauvegarder dans AD seulement si propriétés changées
            if (propsChanged)
            {
                await _adService.UpdateUserAsync(_originalUser);
                _logger.LogInformation("User properties updated in AD.");
            }

            // 4. Synchroniser les groupes (ajouts)
            var currentGroupNames = UserGroups.Select(g => g.GroupName).ToList();
            var groupsToAdd = currentGroupNames.Except(_originalGroupNames).ToList();
            var groupsToRemove = _originalGroupNames.Except(currentGroupNames).ToList();

            foreach (var groupName in groupsToAdd)
            {
                try
                {
                    await _adService.AddUserToGroupAsync(UserName, groupName);
                    changes.Add($"Groupe ajouté : {groupName}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while adding group {Group} for {User}", groupName, UserName);
                }
            }

            // 5. Synchroniser les groupes (retraits)
            foreach (var groupName in groupsToRemove)
            {
                try
                {
                    await _adService.RemoveUserFromGroupAsync(UserName, groupName);
                    changes.Add($"Groupe retiré : {groupName}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while removing group {Group} for {User}", groupName, UserName);
                }
            }

            // 6. Mettre à jour la référence des groupes originaux
            _originalGroupNames = currentGroupNames;
            _originalUser.Groups = UserGroups.ToList();

            // 7. Mettre à jour uniquement cet utilisateur dans le cache
            await _cacheService.CacheUserAsync(_originalUser);

            LastSaveResult = new SaveResultInfo
            {
                Success = true,
                HasChanges = changes.Count > 0,
                Changes = changes
            };

            _logger.LogInformation("User {UserName} saved in AD and cache.", UserName);
        }
        catch (Exception ex)
        {
            SaveError = string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message);
            LastSaveResult = new SaveResultInfo
            {
                Success = false,
                ErrorMessage = ex.Message
            };
            _logger.LogError(ex, "Error while saving user {UserName}", UserName);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task AddSelectedGroupAsync()
    {
        if (SelectedAvailableGroup is null && !string.IsNullOrWhiteSpace(GroupSearchText))
        {
            // Try to find exact match
            SelectedAvailableGroup = _allAvailableGroups.FirstOrDefault(g =>
                g.GroupName.Equals(GroupSearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedAvailableGroup is null) return;
        if (UserGroups.Any(g => g.GroupName.Equals(SelectedAvailableGroup.GroupName, StringComparison.OrdinalIgnoreCase)))
        {
            GroupSearchText = "";
            SelectedAvailableGroup = null;
            return;
        }

        try
        {
            await _adService.AddUserToGroupAsync(UserName, SelectedAvailableGroup.GroupName);
            UserGroups.Add(SelectedAvailableGroup);
            GroupCount = UserGroups.Count;
            _originalGroupNames.Add(SelectedAvailableGroup.GroupName);
            await _cacheService.ClearCacheAsync();

            _logger.LogInformation("Group added: {Group} to {User}", SelectedAvailableGroup.GroupName, UserName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while adding group {Group} to {User}", SelectedAvailableGroup.GroupName, UserName);
        }
        finally
        {
            GroupSearchText = "";
            SelectedAvailableGroup = null;
            IsGroupSearchOpen = false;
        }
    }

    [RelayCommand]
    private async Task ToggleAccountAsync()
    {
        try
        {
            var newState = !IsEnabled;
            var action = newState ? "Enable" : "Disable";
            _logger.LogInformation("Updating account state for {UserName}: {Action}", UserName, action);

            await _adService.SetUserEnabledAsync(UserName, newState);
            IsEnabled = newState;

            _logger.LogInformation("Account updated for {UserName}: {Action}", UserName, action);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while toggling account state for {UserName}", UserName);
        }
    }

    [RelayCommand]
    private async Task RemoveGroupAsync(Group group)
    {
        if (group is null) return;

        try
        {
            await _adService.RemoveUserFromGroupAsync(UserName, group.GroupName);
            UserGroups.Remove(group);
            _originalGroupNames.RemoveAll(g => g.Equals(group.GroupName, StringComparison.OrdinalIgnoreCase));
            GroupCount = UserGroups.Count;
            _logger.LogInformation("Group removed: {GroupName} from {UserName}", group.GroupName, UserName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while removing group {GroupName} from {UserName}", group.GroupName, UserName);
        }
    }
}
