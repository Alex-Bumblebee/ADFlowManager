using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

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
    private readonly IComputerService _computerService;
    private readonly IPackageSigningService _signingService;
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

    [ObservableProperty]
    private bool _loadGroupsOnStartup = true;

    // === Création Utilisateur ===
    [ObservableProperty]
    private string _loginFormat = "Prenom.Nom";

    [ObservableProperty]
    private string _displayNameFormat = "Prenom Nom";

    [ObservableProperty]
    private string _duplicateHandling = "AppendNumber";

    [ObservableProperty]
    private string _emailDomain = "";

    [ObservableProperty]
    private string _passwordPolicy = "Standard";

    public record FormatOption(string Id, string Display)
    {
        public override string ToString() => Display;
    }

    public List<FormatOption> LoginFormats { get; }
    public List<FormatOption> DisplayNameFormats { get; }
    public List<string> DuplicateHandlingOptions { get; } = ["AppendNumber", "DoNothing"];
    public List<FormatOption> PasswordPolicies { get; }

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
    private int _cachedComputersCount;

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

    public List<string> AuditStorageModes { get; }

    // === Templates ===
    [ObservableProperty]
    private int _templateStorageModeIndex;

    [ObservableProperty]
    private string _templateNetworkPath = "";

    [ObservableProperty]
    private string _templateLocalPath = "";

    public List<string> TemplateStorageModes { get; }

    // === Déploiement ===
    [ObservableProperty]
    private string _packageLocalPath = "";

    [ObservableProperty]
    private string _networkPackagesPath = "";

    [ObservableProperty]
    private int _maxGlobalDeployments = 15;

    [ObservableProperty]
    private bool _requireSignedPackages;

    [ObservableProperty]
    private bool _isSigningKeyAvailable;

    [ObservableProperty]
    private string _signingKeyThumbprint = "";

    // === À propos ===
    [ObservableProperty]
    private string _appVersion = "";

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        ISettingsService settingsService,
        ICacheService cacheService,
        IActiveDirectoryService adService,
        IComputerService computerService,
        IPackageSigningService signingService,
        ILocalizationService localization,
        UsersViewModel usersViewModel)
    {
        _logger = logger;
        _settingsService = settingsService;
        _cacheService = cacheService;
        _adService = adService;
        _computerService = computerService;
        _signingService = signingService;
        _localization = localization;
        _usersViewModel = usersViewModel;

        AuditStorageModes = [_localization.GetString("Settings_StorageLocal"), _localization.GetString("Settings_StorageNetwork")];
        TemplateStorageModes = [_localization.GetString("Settings_StorageLocal"), _localization.GetString("Settings_StorageNetwork")];

        LoginFormats =
        [
            new FormatOption("Prenom.Nom", _localization.GetString("Settings_Format_FL")),
            new FormatOption("P.Nom",      _localization.GetString("Settings_Format_FabL")),
            new FormatOption("Nom.P",      _localization.GetString("Settings_Format_LF")),
            new FormatOption("Nom",        _localization.GetString("Settings_Format_L"))
        ];
        DisplayNameFormats =
        [
            new FormatOption("Prenom Nom", _localization.GetString("Settings_Format_FirstLast")),
            new FormatOption("Nom Prenom", _localization.GetString("Settings_Format_LastFirst"))
        ];
        PasswordPolicies =
        [
            new FormatOption("Easy",     _localization.GetString("Settings_Policy_Easy")),
            new FormatOption("Standard", _localization.GetString("Settings_Policy_Standard")),
            new FormatOption("Strong",   _localization.GetString("Settings_Policy_Strong"))
        ];

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
            LoadGroupsOnStartup = s.ActiveDirectory.LoadGroupsOnStartup;
            IncludedUserOUs = string.Join(Environment.NewLine, s.ActiveDirectory.IncludedUserOUs);
            ExcludedUserOUs = string.Join(Environment.NewLine, s.ActiveDirectory.ExcludedUserOUs);

            // Création Utilisateur
            LoginFormat = s.UserCreation.LoginFormat;
            DisplayNameFormat = s.UserCreation.DisplayNameFormat;
            DuplicateHandling = s.UserCreation.DuplicateHandling;
            EmailDomain = s.UserCreation.EmailDomain;
            PasswordPolicy = string.IsNullOrWhiteSpace(s.UserCreation.PasswordPolicy)
                ? "Standard"
                : s.UserCreation.PasswordPolicy;

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

            // Déploiement
            PackageLocalPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ADFlowManager", "Packages", "Local");
            NetworkPackagesPath = s.Deployment.NetworkPackagesPath;
            MaxGlobalDeployments = s.Deployment.MaxGlobalDeployments;
            RequireSignedPackages = s.Deployment.RequireSignedPackages;

            // Signature
            RefreshSigningKeyStatus();

            _logger.LogInformation("Settings loaded into view model.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading settings into view model.");
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
                LoadGroupsOnStartup = LoadGroupsOnStartup,
                IncludedUserOUs = ParseMultilineToList(IncludedUserOUs),
                ExcludedUserOUs = ParseMultilineToList(ExcludedUserOUs)
            },
            UserCreation = new UserCreationSettings
            {
                LoginFormat = LoginFormat,
                DisplayNameFormat = DisplayNameFormat,
                DuplicateHandling = DuplicateHandling,
                EmailDomain = EmailDomain?.Trim() ?? "",
                PasswordPolicy = PasswordPolicy
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
            Deployment = new DeploymentSettings
            {
                NetworkPackagesPath = NetworkPackagesPath?.Trim() ?? "",
                MaxGlobalDeployments = MaxGlobalDeployments,
                RequireSignedPackages = RequireSignedPackages,
                DefaultTimeoutSeconds = _settingsService.CurrentSettings.Deployment.DefaultTimeoutSeconds,
                MaxParallelDeployments = _settingsService.CurrentSettings.Deployment.MaxParallelDeployments
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
            CachedComputersCount = stats.ComputersCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while reading cache stats.");
        }
    }

    // ===== COMMANDS =====

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            // Validation des chemins réseau (path traversal)
            var pathsToValidate = new Dictionary<string, string>();
            if (NetworkLogsEnabled && !string.IsNullOrWhiteSpace(NetworkLogPath))
                pathsToValidate[_localization.GetString("Settings_NetworkLogs")] = NetworkLogPath;
            if (AuditStorageModeIndex == 1 && !string.IsNullOrWhiteSpace(AuditNetworkPath))
                pathsToValidate[_localization.GetString("Settings_AuditNetworkPath")] = AuditNetworkPath;
            if (TemplateStorageModeIndex == 1 && !string.IsNullOrWhiteSpace(TemplateNetworkPath))
                pathsToValidate[_localization.GetString("Settings_NetworkFolder")] = TemplateNetworkPath;
            if (!string.IsNullOrWhiteSpace(NetworkPackagesPath))
                pathsToValidate[_localization.GetString("Settings_PackageStorageNetwork")] = NetworkPackagesPath;

            foreach (var (label, path) in pathsToValidate)
            {
                if (path.Contains("..") || path.Contains("~"))
                {
                    MessageBox.Show(
                        string.Format(_localization.GetString("Settings_InvalidPath"), label),
                        _localization.GetString("Common_Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            // Avertissement bascule vers réseau
            var prev = _settingsService.CurrentSettings;
            bool switchingToNetwork =
                (AuditStorageModeIndex == 1 && prev.Audit.StorageMode != "Network") ||
                (TemplateStorageModeIndex == 1 && prev.Templates.StorageMode != "Network") ||
                (NetworkLogsEnabled && !prev.Logs.NetworkLogsEnabled);

            if (switchingToNetwork)
            {
                var warnResult = MessageBox.Show(
                    _localization.GetString("Settings_NetworkSwitchWarning"),
                    _localization.GetString("Settings_NetworkSwitchTitle"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (warnResult != MessageBoxResult.OK) return;
            }

            // Proposition de création des dossiers réseau manquants
            static string SuggestSubfolder(string path, string subfolder)
            {
                var trimmed = path.TrimEnd('\\', '/');
                var last = Path.GetFileName(trimmed);
                return string.Equals(last, subfolder, StringComparison.OrdinalIgnoreCase)
                    ? trimmed
                    : trimmed + @"\" + subfolder;
            }

            var dirProposals = new List<(string Label, string SuggestedPath, Action<string> Apply)>();

            if (NetworkLogsEnabled && !string.IsNullOrWhiteSpace(NetworkLogPath))
            {
                var suggested = SuggestSubfolder(NetworkLogPath.Trim(), "Logs");
                if (!Directory.Exists(NetworkLogPath.Trim()))
                    dirProposals.Add(("Logs", suggested, p => NetworkLogPath = p));
            }
            if (TemplateStorageModeIndex == 1 && !string.IsNullOrWhiteSpace(TemplateNetworkPath))
            {
                var suggested = SuggestSubfolder(TemplateNetworkPath.Trim(), "Templates");
                if (!Directory.Exists(TemplateNetworkPath.Trim()))
                    dirProposals.Add(("Templates", suggested, p => TemplateNetworkPath = p));
            }
            if (!string.IsNullOrWhiteSpace(NetworkPackagesPath))
            {
                var suggested = SuggestSubfolder(NetworkPackagesPath.Trim(), "Packages");
                if (!Directory.Exists(NetworkPackagesPath.Trim()))
                    dirProposals.Add(("Packages", suggested, p => NetworkPackagesPath = p));
            }
            if (AuditStorageModeIndex == 1 && !string.IsNullOrWhiteSpace(AuditNetworkPath))
            {
                var isFilePath = AuditNetworkPath.Trim().EndsWith(".db", StringComparison.OrdinalIgnoreCase);
                if (isFilePath)
                {
                    var auditDir = Path.GetDirectoryName(AuditNetworkPath.Trim()) ?? "";
                    if (!string.IsNullOrWhiteSpace(auditDir))
                    {
                        var suggestedDir = SuggestSubfolder(auditDir, "History");
                        var fileName = Path.GetFileName(AuditNetworkPath.Trim());
                        var suggestedPath = Path.Combine(suggestedDir, fileName);
                        if (!Directory.Exists(auditDir))
                            dirProposals.Add(("History", suggestedPath, p => AuditNetworkPath = p));
                    }
                }
                else
                {
                    var suggested = SuggestSubfolder(AuditNetworkPath.Trim(), "History");
                    if (!Directory.Exists(AuditNetworkPath.Trim()))
                        dirProposals.Add(("History", suggested + @"\audit-shared.db", p => AuditNetworkPath = p));
                }
            }

            if (dirProposals.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(_localization.GetString("Settings_NetworkDirMissingIntro"));
                sb.AppendLine();
                foreach (var (l, suggested, _) in dirProposals)
                    sb.AppendLine($"  • {l} : {suggested}");
                sb.AppendLine();
                sb.Append(_localization.GetString("Settings_NetworkDirCreatePrompt"));

                var createResult = MessageBox.Show(
                    sb.ToString(),
                    _localization.GetString("Settings_NetworkDirCreateTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (createResult == MessageBoxResult.Yes)
                {
                    foreach (var (_, suggested, apply) in dirProposals)
                    {
                        try
                        {
                            var dirToCreate = suggested.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                                ? Path.GetDirectoryName(suggested)!
                                : suggested;
                            Directory.CreateDirectory(dirToCreate);
                            apply(suggested);
                            _logger.LogInformation("Network directory created: {Path}", dirToCreate);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to create network directory: {Path}", suggested);
                        }
                    }
                }
            }

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

            _logger.LogInformation("Settings saved.");

            // Appliquer le thème immédiatement sans redémarrage
            var appTheme = ThemeIndex == 1 ? ApplicationTheme.Light : ApplicationTheme.Dark;
            ApplicationThemeManager.Apply(appTheme);
            _logger.LogInformation("Theme applied: {Theme}", appTheme);

            if (ouFilterChanged)
            {
                _logger.LogInformation("OU filters changed. Invalidating cache and reloading users.");
                await _usersViewModel.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while saving settings.");
            MessageBox.Show(string.Format(_localization.GetString("Settings_SaveError"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        LoadSettingsFromService();
        _logger.LogInformation("Changes canceled. Settings reloaded.");
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
            CachedComputersCount = 0;

            _logger.LogInformation("Cache cleared.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while clearing cache.");
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

            // Rafraîchir les ordinateurs depuis AD et mettre en cache
            var computers = (await _computerService.GetComputersAsync()).ToList();
            await _cacheService.CacheComputersAsync(computers);

            await LoadCacheStatsAsync();

            _logger.LogInformation("Cache refreshed: {Users} users, {Groups} groups, {Computers} computers",
                CachedUsersCount, CachedGroupsCount, CachedComputersCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while refreshing cache.");
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

            _logger.LogInformation("OUs loaded for selection: {Count}", ous.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading OUs.");
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
        _logger.LogInformation("Opening logs folder.");
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
        _logger.LogInformation("Opening templates folder.");
    }

    // ===== SIGNING KEY MANAGEMENT =====

    private void RefreshSigningKeyStatus()
    {
        try
        {
            IsSigningKeyAvailable = _signingService.IsSigningKeyAvailable();
            SigningKeyThumbprint = IsSigningKeyAvailable
                ? string.Format(_localization.GetString("Settings_SigningThumbprint"),
                    _signingService.GetSigningKeyThumbprint() ?? "")
                : "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking signing key status.");
            IsSigningKeyAvailable = false;
            SigningKeyThumbprint = "";
        }
    }

    [RelayCommand]
    private void GenerateSigningKey()
    {
        var confirm = MessageBox.Show(
            _localization.GetString("Settings_GenerateKeyConfirm"),
            _localization.GetString("Settings_SigningSection"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _signingService.GetOrCreateSigningKey();
            RefreshSigningKeyStatus();

            MessageBox.Show(
                _localization.GetString("Settings_KeyGenerated"),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _logger.LogInformation("Signing key generated from Settings.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating signing key.");
            MessageBox.Show(
                string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ExportSigningKey()
    {
        try
        {
            // Ask for password
            var passwordDialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = _localization.GetString("Settings_ExportKey"),
                Content = new System.Windows.Controls.TextBox
                {
                    Tag = "PfxPasswordBox",
                    MinWidth = 300,
                    FontSize = 14
                },
                PrimaryButtonText = "OK",
                CloseButtonText = _localization.GetString("Common_Cancel")
            };

            // Use SaveFileDialog approach instead for simplicity
            var password = PromptForPassword(_localization.GetString("Settings_PfxPassword"));
            if (string.IsNullOrEmpty(password)) return;

            if (password.Length < 8)
            {
                MessageBox.Show(
                    _localization.GetString("Settings_PfxPassword"),
                    _localization.GetString("Common_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PFX (*.pfx)|*.pfx",
                FileName = $"ADFlowManager-SigningKey-{DateTime.Now:yyyyMMdd}.pfx",
                Title = _localization.GetString("Settings_ExportKey")
            };

            if (dialog.ShowDialog() != true) return;

            _signingService.ExportSigningKey(dialog.FileName, password);

            MessageBox.Show(
                string.Format(_localization.GetString("Settings_KeyExported"), dialog.FileName),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK, MessageBoxImage.Information);

            _logger.LogInformation("Signing key exported.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting signing key.");
            MessageBox.Show(
                string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ImportSigningKey()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PFX (*.pfx)|*.pfx",
                Title = _localization.GetString("Settings_ImportKey")
            };

            if (dialog.ShowDialog() != true) return;

            // Warn if a key already exists — it will be replaced
            if (_signingService.IsSigningKeyAvailable())
            {
                var overwrite = MessageBox.Show(
                    _localization.GetString("Settings_ImportKeyReplaceConfirm"),
                    _localization.GetString("Settings_SigningSection"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (overwrite != MessageBoxResult.Yes) return;
            }

            var password = PromptForPassword(_localization.GetString("Settings_PfxPassword"));
            if (string.IsNullOrEmpty(password)) return;

            _signingService.ImportSigningKey(dialog.FileName, password);
            RefreshSigningKeyStatus();

            MessageBox.Show(
                _localization.GetString("Settings_KeyImported"),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK, MessageBoxImage.Information);

            _logger.LogInformation("Signing key imported.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing signing key.");
            MessageBox.Show(
                string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteSigningKey()
    {
        var confirm = MessageBox.Show(
            _localization.GetString("Settings_DeleteKeyConfirm"),
            _localization.GetString("Settings_SigningSection"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _signingService.DeleteSigningKey();
            RefreshSigningKeyStatus();

            MessageBox.Show(
                _localization.GetString("Settings_KeyDeleted"),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK, MessageBoxImage.Information);

            _logger.LogInformation("Signing key deleted from Settings.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting signing key.");
            MessageBox.Show(
                string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Affiche un InputBox simple pour demander un mot de passe PFX.
    /// Retourne null si annulé.
    /// </summary>
    private static string? PromptForPassword(string prompt)
    {
        var window = new Window
        {
            Title = prompt,
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(30, 30, 30))
        };

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        var label = new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var passwordBox = new System.Windows.Controls.PasswordBox
        {
            Height = 32,
            FontSize = 14
        };
        var okButton = new System.Windows.Controls.Button
        {
            Content = "OK",
            Width = 80,
            Height = 32,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        string? result = null;
        okButton.Click += (_, _) =>
        {
            result = passwordBox.Password;
            window.Close();
        };

        panel.Children.Add(label);
        panel.Children.Add(passwordBox);
        panel.Children.Add(okButton);
        window.Content = panel;
        window.ShowDialog();

        return result;
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

            _logger.LogInformation("Configuration exported.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while exporting configuration.");
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

            _logger.LogInformation("Configuration imported.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while importing configuration.");
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
