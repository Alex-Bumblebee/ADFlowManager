using System.Collections.ObjectModel;
using System.Reflection;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// ViewModel du tableau de bord principal.
/// Affiche les stats AD, l'activité récente et les actions rapides.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly IActiveDirectoryService _adService;
    private readonly IAuditService _auditService;
    private readonly INavigationService _navigationService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<DashboardViewModel> _logger;

    // === Branding ===
    [ObservableProperty]
    private string _appVersion = "";

    [ObservableProperty]
    private string _currentUser = "";

    [ObservableProperty]
    private string _currentDomain = "";

    [ObservableProperty]
    private string _connectedUser = "";

    // === Stats ===
    [ObservableProperty]
    private int _totalUsers;

    [ObservableProperty]
    private int _totalGroups;

    [ObservableProperty]
    private int _actionsToday;

    [ObservableProperty]
    private bool _hasNoActions = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<ActivityItem> _recentActivities = [];

    public DashboardViewModel(
        IActiveDirectoryService adService,
        IAuditService auditService,
        INavigationService navigationService,
        ILocalizationService localization,
        ILogger<DashboardViewModel> logger)
    {
        _adService = adService;
        _auditService = auditService;
        _navigationService = navigationService;
        _localization = localization;
        _logger = logger;

        // Version assembly auto (sync avec .csproj)
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";

        // User info
        CurrentDomain = _adService.ConnectedDomain ?? Environment.UserDomainName;
        CurrentUser = _adService.ConnectedUser ?? Environment.UserName;
        ConnectedUser = $"{CurrentUser}@{CurrentDomain}";

        _logger.LogInformation("Dashboard loaded: version {Version}, user {User}@{Domain}",
            AppVersion, CurrentUser, CurrentDomain);

        _ = LoadDashboardDataAsync();
    }

    private async Task LoadDashboardDataAsync()
    {
        IsLoading = true;
        try
        {
            var users = await _adService.GetUsersAsync();
            TotalUsers = users.Count;

            var groups = await _adService.GetGroupsAsync();
            TotalGroups = groups.Count;

            // Stats audit
            var stats = await _auditService.GetStatsAsync();
            ActionsToday = stats.LogsToday;

            // 10 dernières actions réelles
            var logs = await _auditService.GetLogsAsync(limit: 10);
            RecentActivities = new ObservableCollection<ActivityItem>(
                logs.Select(l => new ActivityItem
                {
                    Description = $"{l.ActionType} — {l.EntityDisplayName}",
                    TimeAgo = FormatTimeAgo(l.Timestamp)
                }));

            HasNoActions = RecentActivities.Count == 0;

            _logger.LogInformation("Dashboard loaded: {Users} users, {Groups} groups, {Actions} actions today",
                TotalUsers, TotalGroups, ActionsToday);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading dashboard.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string FormatTimeAgo(DateTime timestamp)
    {
        var diff = DateTime.Now - timestamp;
        if (diff.TotalMinutes < 1) return _localization.GetString("Time_JustNow");
        if (diff.TotalMinutes < 60) return string.Format(_localization.GetString("Time_MinutesAgo"), (int)diff.TotalMinutes);
        if (diff.TotalHours < 24) return string.Format(_localization.GetString("Time_HoursAgo"), (int)diff.TotalHours);
        if (diff.TotalDays < 7) return string.Format(_localization.GetString("Time_DaysAgo"), (int)diff.TotalDays);
        return timestamp.ToString("dd/MM/yyyy HH:mm");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger.LogInformation("Refreshing dashboard.");
        await LoadDashboardDataAsync();
    }

    [RelayCommand]
    private void Disconnect()
    {
        var result = System.Windows.MessageBox.Show(
            _localization.GetString("Disconnect_Confirm"),
            _localization.GetString("Disconnect_Title"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _logger.LogInformation("User requested disconnect.");
            System.Windows.Application.Current.Shutdown();
        }
    }

    [RelayCommand]
    private void NewUser()
    {
        _navigationService.Navigate(typeof(Views.Pages.CreateUserPage));
    }

    [RelayCommand]
    private void NewGroup()
    {
        _navigationService.Navigate(typeof(Views.Pages.GroupsPage));
    }

    [RelayCommand]
    private void OpenTemplates()
    {
        _navigationService.Navigate(typeof(Views.Pages.TemplatesPage));
    }

    [RelayCommand]
    private void OpenHistory()
    {
        _navigationService.Navigate(typeof(Views.Pages.HistoriquePage));
    }
}

/// <summary>
/// Élément d'activité récente affiché sur le dashboard.
/// </summary>
public class ActivityItem
{
    public string Description { get; set; } = "";
    public string TimeAgo { get; set; } = "";
}
