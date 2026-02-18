using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// ViewModel de la page Paramètres.
/// Gère 5 onglets : Général, Active Directory, Cache, Logs, À propos.
/// Persistence via ISettingsService (JSON).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly ICacheService _cacheService;
    private readonly IActiveDirectoryService _adService;
    private readonly ILocalizationService _localization;
    private readonly UsersViewModel _usersViewModel;

    // === Navigation ===
    [ObservableProperty]
    private int _selectedTab;

    // === Général ===
    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private int _languageIndex;

    // === Active Directory ===
    [ObservableProperty]
    private string _defaultUserOU = "";

    [ObservableProperty]
    private string _defaultGroupOU = "";

    [ObservableProperty]
    private string _includedUserOUs = "";

    [ObservableProperty]
    private string _excludedUserOUs = "";

    [ObservableProperty]
    private string _disabledUserOU = "";

    /// <summary>
    /// Liste des OUs disponibles dans le domaine AD (chargées à la demande).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<OrganizationalUnitInfo> _availableOUs = [];

    // === Cache ===
    [ObservableProperty]
    private bool _cacheEnabled = true;

    [ObservableProperty]
    private double _cacheRefreshMinutes = 120;

    [ObservableProperty]
    private string _lastCacheRefresh = "Jamais";

    [ObservableProperty]
    private int _cachedUsersCount;

    [ObservableProperty]
    private int _cachedGroupsCount;

    [ObservableProperty]
    private bool _isCacheRefreshing;

    // === Logs ===
    [ObservableProperty]
    private bool _networkLogsEnabled;

    [ObservableProperty]
    private string _networkLogPath = "";

    // === Historique (Audit) ===
    [ObservableProperty]
    private bool _auditEnabled = true;

    [ObservableProperty]
    private int _auditStorageModeIndex;

    [ObservableProperty]
    private string _auditNetworkPath = "";

    [ObservableProperty]
    private int _auditRetentionDays = 0;

    public List<string> AuditStorageModes { get; } = ["Local", "Réseau partagé"];

    // === Templates ===
    [ObservableProperty]
    private int _templateStorageModeIndex;

    [ObservableProperty]
    private string _templateNetworkPath = "";

    [ObservableProperty]
    private string _templateLocalPath = "";

    public List<string> TemplateStorageModes { get; } = ["Local", "Réseau partagé"];

    // === À propos ===
    [ObservableProperty]
    private string _appVersion = "";

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        ISettingsService settingsService,
        ICacheService cacheService,
        IActiveDirectoryService adService,
        ILocalizationService localization,
        UsersViewModel usersViewModel)
    {
        _logger = logger;
        _settingsService = settingsService;
        _cacheService = cacheService;
        _adService = adService;
        _localization = localization;
        _usersViewModel = usersViewModel;

        AppVersion = GetAssemblyVersion();
        LoadSettingsFromService();
        _ = LoadCacheStatsAsync();
    }

    private static string GetAssemblyVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
    }

    // ===== CHARGEMENT SETTINGS =====

    private void LoadSettingsFromService()
    {
        try
        {
            var s = _settingsService.CurrentSettings;

            // Général
            ThemeIndex = s.General.ThemeIndex;
            LanguageIndex = s.General.LanguageIndex;

            // Active Directory
            DefaultUserOU = s.ActiveDirectory.DefaultUserOU;
            DefaultGroupOU = s.ActiveDirectory.DefaultGroupOU;
            DisabledUserOU = s.ActiveDirectory.DisabledUserOU;
            IncludedUserOUs = string.Join(Environment.NewLine, s.ActiveDirectory.IncludedUserOUs);
            ExcludedUserOUs = string.Join(Environment.NewLine, s.ActiveDirectory.ExcludedUserOUs);

            // Cache
            CacheEnabled = s.Cache.IsEnabled;
            CacheRefreshMinutes = s.Cache.TtlMinutes;

            // Logs
            NetworkLogsEnabled = s.Logs.NetworkLogsEnabled;
            NetworkLogPath = s.Logs.NetworkLogPath;

            // Audit
            AuditEnabled = s.Audit.IsEnabled;
            AuditStorageModeIndex = s.Audit.StorageMode == "Network" ? 1 : 0;
            AuditNetworkPath = s.Audit.NetworkDatabasePath;
            AuditRetentionDays = s.Audit.RetentionDays;

            // Templates
            TemplateStorageModeIndex = s.Templates.StorageMode == "Network" ? 1 : 0;
            TemplateNetworkPath = s.Templates.NetworkFolderPath;
            TemplateLocalPath = s.Templates.LocalFolderPath;

            _logger.LogInformation("Settings chargés dans ViewModel");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement settings dans ViewModel");
        }
    }

    private AppSettings BuildSettingsFromViewModel()
    {
        return new AppSettings
        {
            General = new GeneralSettings
            {
                ThemeIndex = ThemeIndex,
                LanguageIndex = LanguageIndex
            },
            ActiveDirectory = new ActiveDirectorySettings
            {
                DefaultUserOU = DefaultUserOU?.Trim() ?? "",
                DefaultGroupOU = DefaultGroupOU?.Trim() ?? "",
                DisabledUserOU = DisabledUserOU?.Trim() ?? "",
                IncludedUserOUs = ParseMultilineToList(IncludedUserOUs),
                ExcludedUserOUs = ParseMultilineToList(ExcludedUserOUs)
            },
            Cache = new CacheSettings
            {
                IsEnabled = CacheEnabled,
                TtlMinutes = CacheRefreshMinutes
            },
            Logs = new LogSettings
            {
                NetworkLogsEnabled = NetworkLogsEnabled,
                NetworkLogPath = NetworkLogPath?.Trim() ?? ""
            },
            Audit = new AuditSettings
            {
                IsEnabled = AuditEnabled,
                StorageMode = AuditStorageModeIndex == 1 ? "Network" : "Local",
                NetworkDatabasePath = AuditNetworkPath?.Trim() ?? "",
                RetentionDays = AuditRetentionDays
            },
            Templates = new TemplateSettings
            {
                StorageMode = TemplateStorageModeIndex == 1 ? "Network" : "Local",
                NetworkFolderPath = TemplateNetworkPath?.Trim() ?? ""
            },
            ConfigVersion = _settingsService.CurrentSettings.ConfigVersion
        };
    }

    private static List<string> ParseMultilineToList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    // ===== CACHE STATS =====

    private async Task LoadCacheStatsAsync()
    {
        try
        {
            var stats = await _cacheService.GetCacheStatsAsync();

            if (stats.UsersLastRefresh.HasValue)
            {
                LastCacheRefresh = stats.UsersLastRefresh.Value.ToString("dd/MM/yyyy HH:mm");
                CachedUsersCount = stats.UsersCount;
            }
            else
            {
                LastCacheRefresh = _localization.GetString("Settings_CacheNever");
                CachedUsersCount = 0;
            }

            CachedGroupsCount = stats.GroupsCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lecture stats cache");
        }
    }

    // ===== COMMANDS =====

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var prevIncluded = _settingsService.CurrentSettings.ActiveDirectory.IncludedUserOUs;
            var prevExcluded = _settingsService.CurrentSettings.ActiveDirectory.ExcludedUserOUs;

            var settings = BuildSettingsFromViewModel();
            await _settingsService.SaveSettingsAsync(settings);

            var ouFilterChanged =
                !prevIncluded.SequenceEqual(settings.ActiveDirectory.IncludedUserOUs) ||
                !prevExcluded.SequenceEqual(settings.ActiveDirectory.ExcludedUserOUs);

            MessageBox.Show(
                _localization.GetString("Settings_SaveSuccess"),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _logger.LogInformation("Settings sauvegardés");

            if (ouFilterChanged)
            {
                _logger.LogInformation("Filtres OU modifiés — invalidation cache et rechargement utilisateurs");
                await _usersViewModel.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur sauvegarde settings");
            MessageBox.Show(string.Format(_localization.GetString("Settings_SaveError"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        LoadSettingsFromService();
        _logger.LogInformation("Modifications annulées, settings rechargés");
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var result = MessageBox.Show(
            _localization.GetString("Settings_CacheClearConfirm"),
            _localization.GetString("Settings_CacheClearTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _cacheService.ClearCacheAsync();

            LastCacheRefresh = _localization.GetString("Settings_CacheNever");
            CachedUsersCount = 0;
            CachedGroupsCount = 0;

            _logger.LogInformation("Cache vidé");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur vidage cache");
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshCacheAsync()
    {
        if (IsCacheRefreshing) return;

        try
        {
            IsCacheRefreshing = true;

            await _cacheService.ClearCacheAsync();

            await _adService.GetUsersAsync();
            await _adService.GetGroupsAsync();

            await LoadCacheStatsAsync();

            _logger.LogInformation("Cache rafraîchi : {Users} users, {Groups} groups",
                CachedUsersCount, CachedGroupsCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur rafraîchissement cache");
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsCacheRefreshing = false;
        }
    }

    // ===== OU BROWSE COMMANDS =====

    private async Task EnsureOUsLoadedAsync()
    {
        if (AvailableOUs.Count > 0) return;

        try
        {
            if (!_adService.IsConnected)
            {
                MessageBox.Show(_localization.GetString("Settings_NotConnected"), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var ous = await _adService.GetOrganizationalUnitsAsync();
            AvailableOUs = new ObservableCollection<OrganizationalUnitInfo>(
                ous.OrderBy(o => o.Path));

            _logger.LogInformation("OUs chargées pour sélection : {Count}", ous.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement OUs");
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task BrowseDefaultUserOUAsync()
    {
        await EnsureOUsLoadedAsync();
        var selected = ShowOUSelectionDialog(_localization.GetString("Settings_BrowseUserOU"), DefaultUserOU);
        if (selected != null) DefaultUserOU = selected;
    }

    [RelayCommand]
    private async Task BrowseDefaultGroupOUAsync()
    {
        await EnsureOUsLoadedAsync();
        var selected = ShowOUSelectionDialog(_localization.GetString("Settings_BrowseGroupOU"), DefaultGroupOU);
        if (selected != null) DefaultGroupOU = selected;
    }

    [RelayCommand]
    private async Task BrowseDisabledUserOUAsync()
    {
        await EnsureOUsLoadedAsync();
        var selected = ShowOUSelectionDialog(_localization.GetString("Settings_BrowseDisabledOU"), DisabledUserOU);
        if (selected != null) DisabledUserOU = selected;
    }

    [RelayCommand]
    private async Task BrowseIncludedOUAsync()
    {
        await EnsureOUsLoadedAsync();
        var selected = ShowOUSelectionDialog(_localization.GetString("Settings_BrowseIncludedOU"), "");
        if (selected != null)
        {
            if (!string.IsNullOrWhiteSpace(IncludedUserOUs))
                IncludedUserOUs += Environment.NewLine + selected;
            else
                IncludedUserOUs = selected;
        }
    }

    [RelayCommand]
    private async Task BrowseExcludedOUAsync()
    {
        await EnsureOUsLoadedAsync();
        var selected = ShowOUSelectionDialog(_localization.GetString("Settings_BrowseExcludedOU"), "");
        if (selected != null)
        {
            if (!string.IsNullOrWhiteSpace(ExcludedUserOUs))
                ExcludedUserOUs += Environment.NewLine + selected;
            else
                ExcludedUserOUs = selected;
        }
    }

    /// <summary>
    /// Affiche un dialog simple de sélection d'OU parmi la liste chargée.
    /// </summary>
    private string? ShowOUSelectionDialog(string title, string currentValue)
    {
        if (AvailableOUs.Count == 0)
        {
            MessageBox.Show(_localization.GetString("Settings_NoOUs"), _localization.GetString("Common_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var window = new System.Windows.Window
        {
            Title = title,
            Width = 600,
            Height = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
            ResizeMode = ResizeMode.CanResize
        };

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        // Search box
        var searchBox = new System.Windows.Controls.TextBox
        {
            Margin = new Thickness(12),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30)),
        };
        System.Windows.Controls.Grid.SetRow(searchBox, 0);
        grid.Children.Add(searchBox);

        // ListBox
        var listBox = new System.Windows.Controls.ListBox
        {
            Margin = new Thickness(12, 0, 12, 12),
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30)),
            FontSize = 13
        };

        void RefreshList(string filter)
        {
            listBox.Items.Clear();
            var filtered = string.IsNullOrWhiteSpace(filter)
                ? AvailableOUs
                : AvailableOUs.Where(o =>
                    o.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    o.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));

            foreach (var ou in filtered)
            {
                var item = new System.Windows.Controls.ListBoxItem
                {
                    Content = ou.DisplayName,
                    Tag = ou.Path,
                    Foreground = System.Windows.Media.Brushes.White,
                    Padding = new Thickness(8, 6, 8, 6),
                    ToolTip = ou.Path
                };
                listBox.Items.Add(item);
            }
        }

        RefreshList("");
        searchBox.TextChanged += (s, e) => RefreshList(searchBox.Text);

        System.Windows.Controls.Grid.SetRow(listBox, 1);
        grid.Children.Add(listBox);

        // Buttons
        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12)
        };

        string? result = null;

        var cancelBtn = new System.Windows.Controls.Button { Content = _localization.GetString("Common_Cancel"), Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (s, e) => window.Close();
        btnPanel.Children.Add(cancelBtn);

        var okBtn = new System.Windows.Controls.Button { Content = _localization.GetString("Common_Select"), Padding = new Thickness(20, 6, 20, 6) };
        okBtn.Click += (s, e) =>
        {
            if (listBox.SelectedItem is System.Windows.Controls.ListBoxItem selected)
            {
                result = selected.Tag?.ToString();
                window.Close();
            }
        };
        btnPanel.Children.Add(okBtn);

        listBox.MouseDoubleClick += (s, e) =>
        {
            if (listBox.SelectedItem is System.Windows.Controls.ListBoxItem selected)
            {
                result = selected.Tag?.ToString();
                window.Close();
            }
        };

        System.Windows.Controls.Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        window.Content = grid;
        window.ShowDialog();

        return result;
    }

    [RelayCommand]
    private void OpenLogs()
    {
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ADFlowManager",
            "logs");

        if (!Directory.Exists(logsPath))
            Directory.CreateDirectory(logsPath);

        Process.Start("explorer.exe", logsPath);
        _logger.LogInformation("Ouverture dossier logs");
    }

    [RelayCommand]
    private void OpenAuditDb()
    {
        var s = _settingsService.CurrentSettings;
        var dbPath = s.Audit.StorageMode == "Network" && !string.IsNullOrWhiteSpace(s.Audit.NetworkDatabasePath)
            ? s.Audit.NetworkDatabasePath
            : s.Audit.LocalDatabasePath;

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
        else
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), dir), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    private void OpenTemplatesFolder()
    {
        var s = _settingsService.CurrentSettings.Templates;
        var path = s.StorageMode == "Network" && !string.IsNullOrWhiteSpace(s.NetworkFolderPath)
            ? s.NetworkFolderPath
            : s.LocalFolderPath;

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        Process.Start("explorer.exe", path);
        _logger.LogInformation("Ouverture dossier templates : {Path}", path);
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/Alex-Bumblebee/ADFlowManager",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Fichiers JSON (*.json)|*.json",
                FileName = $"ADFlowManager-Settings-{DateTime.Now:yyyyMMdd}.json",
                Title = _localization.GetString("Settings_ExportTitle")
            };

            if (dialog.ShowDialog() != true) return;

            // Sauvegarder d'abord les valeurs actuelles du ViewModel
            var settings = BuildSettingsFromViewModel();
            await _settingsService.SaveSettingsAsync(settings);

            await _settingsService.ExportSettingsAsync(dialog.FileName);

            MessageBox.Show(
                string.Format(_localization.GetString("Settings_ExportSuccess"), dialog.FileName),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _logger.LogInformation("Config exportée : {Path}", dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur export config");
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Fichiers JSON (*.json)|*.json",
                Title = _localization.GetString("Settings_ImportTitle")
            };

            if (dialog.ShowDialog() != true) return;

            var confirm = MessageBox.Show(
                _localization.GetString("Settings_ImportConfirm"),
                _localization.GetString("Settings_ImportTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            await _settingsService.ImportSettingsAsync(dialog.FileName);

            LoadSettingsFromService();

            MessageBox.Show(
                _localization.GetString("Settings_ImportSuccess"),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _logger.LogInformation("Config importée : {Path}", dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur import config");
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
