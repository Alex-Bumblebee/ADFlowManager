using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.ViewModels.Dialogs;

/// <summary>
/// ViewModel du dialog de comparaison de groupes entre 2 utilisateurs.
/// Affiche 3 colonnes : Unique User 1 (bleu), Communs (violet), Unique User 2 (orange).
/// Permet de copier des groupes individuellement ou en batch.
/// </summary>
public partial class CompareUsersDialogViewModel : ObservableObject
{
    private readonly IActiveDirectoryService _adService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<CompareUsersDialogViewModel> _logger;

    private User _user1 = null!;
    private User _user2 = null!;

    // === User 1 ===
    [ObservableProperty]
    private string _user1DisplayName = "";

    [ObservableProperty]
    private string _user1UserName = "";

    [ObservableProperty]
    private string _user1Initials = "??";

    // === User 2 ===
    [ObservableProperty]
    private string _user2DisplayName = "";

    [ObservableProperty]
    private string _user2UserName = "";

    [ObservableProperty]
    private string _user2Initials = "??";

    // === Groupes ===
    [ObservableProperty]
    private ObservableCollection<Group> _user1UniqueGroups = [];

    [ObservableProperty]
    private ObservableCollection<Group> _commonGroups = [];

    [ObservableProperty]
    private ObservableCollection<Group> _user2UniqueGroups = [];

    // === Compteurs ===
    [ObservableProperty]
    private int _user1UniqueCount;

    [ObservableProperty]
    private int _commonGroupsCount;

    [ObservableProperty]
    private int _user2UniqueCount;

    [ObservableProperty]
    private bool _isApplying;

    [ObservableProperty]
    private string _statusMessage = "";

    // Track pending AD changes: (samAccountName, groupName)
    private readonly List<(string Sam, string GroupName)> _pendingAdds = [];

    public bool HasChanges => _pendingAdds.Count > 0;

    public CompareUsersDialogViewModel(
        IActiveDirectoryService adService,
        ICacheService cacheService,
        ILogger<CompareUsersDialogViewModel> logger)
    {
        _adService = adService;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Initialise la comparaison entre deux utilisateurs.
    /// </summary>
    public void Initialize(User user1, User user2)
    {
        _user1 = user1;
        _user2 = user2;

        User1DisplayName = user1.DisplayName;
        User1UserName = user1.UserName;
        User1Initials = GetInitials(user1);

        User2DisplayName = user2.DisplayName;
        User2UserName = user2.UserName;
        User2Initials = GetInitials(user2);

        CompareGroups();

        _logger.LogInformation("Comparaison : {User1} vs {User2}", user1.UserName, user2.UserName);
    }

    private string GetInitials(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(user.LastName))
            return $"{user.FirstName[0]}{user.LastName[0]}".ToUpper();
        if (!string.IsNullOrWhiteSpace(user.DisplayName) && user.DisplayName.Contains(' '))
        {
            var parts = user.DisplayName.Split(' ');
            return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
        }
        return user.DisplayName.Length >= 2 ? user.DisplayName[..2].ToUpper() : "??";
    }

    private void CompareGroups()
    {
        var user1GroupNames = _user1.Groups.Select(g => g.GroupName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var user2GroupNames = _user2.Groups.Select(g => g.GroupName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        User1UniqueGroups = new ObservableCollection<Group>(
            _user1.Groups.Where(g => !user2GroupNames.Contains(g.GroupName)));

        CommonGroups = new ObservableCollection<Group>(
            _user1.Groups.Where(g => user2GroupNames.Contains(g.GroupName)));

        User2UniqueGroups = new ObservableCollection<Group>(
            _user2.Groups.Where(g => !user1GroupNames.Contains(g.GroupName)));

        UpdateCounts();

        _logger.LogInformation("Resultat : {U1} unique, {Common} communs, {U2} unique",
            User1UniqueCount, CommonGroupsCount, User2UniqueCount);
    }

    private void UpdateCounts()
    {
        User1UniqueCount = User1UniqueGroups.Count;
        CommonGroupsCount = CommonGroups.Count;
        User2UniqueCount = User2UniqueGroups.Count;
    }

    // === Actions individuelles ===

    [RelayCommand]
    private void CopyToUser2(Group group)
    {
        User1UniqueGroups.Remove(group);
        CommonGroups.Add(group);
        _pendingAdds.Add((_user2.UserName, group.GroupName));
        UpdateCounts();
        OnPropertyChanged(nameof(HasChanges));
        _logger.LogInformation("Groupe {Group} planifié : {User1} -> {User2}", group.GroupName, _user1.UserName, _user2.UserName);
    }

    [RelayCommand]
    private void CopyToUser1(Group group)
    {
        User2UniqueGroups.Remove(group);
        CommonGroups.Add(group);
        _pendingAdds.Add((_user1.UserName, group.GroupName));
        UpdateCounts();
        OnPropertyChanged(nameof(HasChanges));
        _logger.LogInformation("Groupe {Group} planifié : {User2} -> {User1}", group.GroupName, _user2.UserName, _user1.UserName);
    }

    // === Actions batch ===

    [RelayCommand]
    private void CopyAllToUser2()
    {
        foreach (var group in User1UniqueGroups.ToList())
        {
            _pendingAdds.Add((_user2.UserName, group.GroupName));
            User1UniqueGroups.Remove(group);
            CommonGroups.Add(group);
        }
        UpdateCounts();
        OnPropertyChanged(nameof(HasChanges));
        _logger.LogInformation("Tous les groupes planifiés : {User1} -> {User2}", _user1.UserName, _user2.UserName);
    }

    [RelayCommand]
    private void CopyAllToUser1()
    {
        foreach (var group in User2UniqueGroups.ToList())
        {
            _pendingAdds.Add((_user1.UserName, group.GroupName));
            User2UniqueGroups.Remove(group);
            CommonGroups.Add(group);
        }
        UpdateCounts();
        OnPropertyChanged(nameof(HasChanges));
        _logger.LogInformation("Tous les groupes planifiés : {User2} -> {User1}", _user2.UserName, _user1.UserName);
    }

    [RelayCommand]
    private void SyncBoth()
    {
        foreach (var group in User1UniqueGroups.ToList())
        {
            _pendingAdds.Add((_user2.UserName, group.GroupName));
            User1UniqueGroups.Remove(group);
            CommonGroups.Add(group);
        }
        foreach (var group in User2UniqueGroups.ToList())
        {
            _pendingAdds.Add((_user1.UserName, group.GroupName));
            User2UniqueGroups.Remove(group);
            CommonGroups.Add(group);
        }
        UpdateCounts();
        OnPropertyChanged(nameof(HasChanges));
        _logger.LogInformation("Egalisation planifiée : {User1} <-> {User2}",
            _user1.UserName, _user2.UserName);
    }

    /// <summary>
    /// Applique toutes les modifications planifiées dans AD.
    /// </summary>
    [RelayCommand]
    private async Task ApplyChangesAsync()
    {
        if (_pendingAdds.Count == 0) return;

        IsApplying = true;
        StatusMessage = "";
        var successCount = 0;
        var errorCount = 0;

        try
        {
            foreach (var (sam, groupName) in _pendingAdds)
            {
                try
                {
                    await _adService.AddUserToGroupAsync(sam, groupName);
                    successCount++;
                    _logger.LogInformation("✅ Groupe {Group} ajouté à {User}", groupName, sam);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.LogWarning(ex, "❌ Erreur ajout {Group} à {User}", groupName, sam);
                }
            }

            _pendingAdds.Clear();
            OnPropertyChanged(nameof(HasChanges));

            await _cacheService.ClearCacheAsync();

            StatusMessage = errorCount == 0
                ? $"{successCount} groupe(s) appliqué(s) avec succès"
                : $"{successCount} succès, {errorCount} erreur(s)";

            _logger.LogInformation("Résultat application : {Success} succès, {Errors} erreurs", successCount, errorCount);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur : {ex.Message}";
            _logger.LogError(ex, "Erreur application des modifications");
        }
        finally
        {
            IsApplying = false;
        }
    }
}
