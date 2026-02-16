using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using ADFlowManager.Infrastructure.ActiveDirectory.Services;
using ADFlowManager.Infrastructure.Security;
using ADFlowManager.Infrastructure.Services;
using ADFlowManager.UI.Services;
using ADFlowManager.UI.ViewModels.Pages;
using ADFlowManager.UI.ViewModels.Windows;
using ADFlowManager.UI.Views.Pages;
using ADFlowManager.UI.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.DependencyInjection;

namespace ADFlowManager.UI
{
    /// <summary>
    /// Point d'entrée de l'application WPF avec configuration DI, logging et services.
    /// </summary>
    public partial class App
    {
        /// <summary>
        /// Velopack DOIT être initialisé avant toute autre chose.
        /// Le constructeur statique s'exécute avant les champs statiques.
        /// </summary>
        static App()
        {
            VelopackApp.Build()
                .WithFirstRun((v) =>
                {
                    MessageBox.Show(
                        $"ADFlowManager v{v}\n\n" +
                        "Mises à jour automatiques activées.",
                        "Première installation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                })
                .Run();
        }

        // Configuration de Serilog pour le logging de l'application
        // Logs dual : Local (obligatoire) + Réseau (optionnel)
        private static readonly ILogger _logger = ConfigureLogger();

        /// <summary>
        /// Configure Serilog avec système de logs dual (local + réseau optionnel).
        /// - Logs locaux : TOUJOURS actifs dans %LOCALAPPDATA%\ADFlowManager\logs\
        /// - Logs réseau : OPTIONNELS si NetworkLogPath configuré et accessible
        /// </summary>
        private static ILogger ConfigureLogger()
        {
            var settings = LoadLoggingSettings();

            // Créer le dossier de logs locaux s'il n'existe pas
            Directory.CreateDirectory(settings.LocalLogPath);

            var logConfig = new LoggerConfiguration()
                .MinimumLevel.Is(ParseLogLevel(settings.MinimumLevel));

            // === Sink 1 : Console (Debug) ===
            logConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

            // === Sink 2 : Fichier Local (TOUJOURS actif) ===
            var localLogFile = Path.Combine(settings.LocalLogPath, "adflow-.log");
            logConfig.WriteTo.File(
                path: localLogFile,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: settings.LocalRetentionDays,
                shared: true);

            // === Sink 3 : Fichier Réseau (OPTIONNEL) ===
            if (!string.IsNullOrWhiteSpace(settings.NetworkLogPath))
            {
                try
                {
                    // Remplacer {username} puis utiliser le dossier tel que configuré
                    var networkLogDir = Environment.ExpandEnvironmentVariables(
                        settings.NetworkLogPath.Replace("{username}", Environment.UserName).Trim());

                    // Tenter de créer le dossier réseau (test d'accessibilité)
                    Directory.CreateDirectory(networkLogDir);

                    // Fichier distinct par machine/utilisateur pour éviter les collisions
                    var networkLogFile = Path.Combine(
                        networkLogDir,
                        $"adflow-{Environment.MachineName}-{Environment.UserName}-.log");
                    logConfig.WriteTo.File(
                        path: networkLogFile,
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] [{MachineName}] {Message:lj}{NewLine}{Exception}",
                        retainedFileCountLimit: settings.NetworkRetentionDays,
                        shared: true);

                }
                catch (Exception ex)
                {
                    // Si le réseau est inaccessible, continuer avec logs locaux uniquement
                    // L'erreur sera loggée dans OnStartup via _logger.Warning()
                    Console.WriteLine($"[WARNING] Logs réseau désactivés : {ex.Message}");
                }
            }

            return logConfig.CreateLogger();
        }

        /// <summary>
        /// Charge la configuration de logging depuis settings.json.
        /// Fallback automatique sur des valeurs locales si le fichier est absent/invalide.
        /// </summary>
        private static LoggingSettings LoadLoggingSettings()
        {
            var defaults = new LoggingSettings
            {
                LocalLogPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ADFlowManager",
                    "logs"),
                NetworkLogPath = null,
                MinimumLevel = "Information",
                LocalRetentionDays = 30,
                NetworkRetentionDays = 90
            };

            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ADFlowManager",
                    "settings.json");

                if (!File.Exists(settingsPath))
                    return defaults;

                var json = File.ReadAllText(settingsPath);
                var appSettings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (appSettings?.Logs == null)
                    return defaults;

                defaults.NetworkLogPath = appSettings.Logs.NetworkLogsEnabled &&
                                          !string.IsNullOrWhiteSpace(appSettings.Logs.NetworkLogPath)
                    ? appSettings.Logs.NetworkLogPath.Trim()
                    : null;

                return defaults;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Impossible de charger settings logging : {ex.Message}");
                return defaults;
            }
        }

        /// <summary>
        /// Parse le niveau de log depuis string vers LogEventLevel.
        /// </summary>
        private static LogEventLevel ParseLogLevel(string level)
        {
            return level.ToLower() switch
            {
                "verbose" => LogEventLevel.Verbose,
                "debug" => LogEventLevel.Debug,
                "information" => LogEventLevel.Information,
                "warning" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error,
                "fatal" => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
        }

        // Configuration du Host .NET pour DI, configuration et services
        // https://docs.microsoft.com/dotnet/core/extensions/generic-host
        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)!); })
            .UseSerilog(_logger)
            .ConfigureServices((context, services) =>
            {
                // === WPF-UI Services ===
                services.AddNavigationViewPageProvider();
                services.AddHostedService<ApplicationHostService>();

                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<ITaskBarService, TaskBarService>();
                services.AddSingleton<INavigationService, NavigationService>();

                // === Core Services (Business Logic) ===
                // Localization (Singleton - avant tout car dépendance UI)
                services.AddSingleton<ILocalizationService, LocalizationService>();

                // Settings JSON (Singleton - avant Cache car dépendance TTL)
                services.AddSingleton<ISettingsService, SettingsService>();

                // Cache SQLite (Singleton - avant AD car dépendance)
                services.AddSingleton<ICacheService, CacheService>();

                // Audit SQLite (Singleton - après Settings)
                services.AddSingleton<IAuditService, AuditService>();

                // Templates JSON (Singleton - après Settings)
                services.AddSingleton<ITemplateService, TemplateService>();

                services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
                services.AddSingleton<ICredentialService, CredentialService>();

                // === Windows & Pages ===
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                services.AddTransient<LoginWindow>();
                services.AddTransient<LoginPage>();
                services.AddTransient<LoginViewModel>();

                services.AddSingleton<DashboardPage>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<UsersPage>();
                services.AddSingleton<UsersViewModel>();
                services.AddSingleton<GroupsPage>();
                services.AddSingleton<GroupsViewModel>();
                services.AddSingleton<CreateUserPage>();
                services.AddSingleton<CreateUserViewModel>();
                services.AddTransient<UserDetailsWindow>();
                services.AddTransient<UserDetailsViewModel>();
                services.AddTransient<Views.Dialogs.CopyRightsDialog>();
                services.AddTransient<ViewModels.Dialogs.CopyRightsDialogViewModel>();
                services.AddTransient<Views.Dialogs.ResetPasswordDialog>();
                services.AddTransient<ViewModels.Dialogs.ResetPasswordDialogViewModel>();
                services.AddTransient<Views.Dialogs.CompareUsersDialog>();
                services.AddTransient<ViewModels.Dialogs.CompareUsersDialogViewModel>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<Views.Pages.HistoriquePage>();
                services.AddSingleton<ViewModels.Pages.HistoriqueViewModel>();
                services.AddSingleton<Views.Pages.TemplatesPage>();
                services.AddSingleton<ViewModels.Pages.TemplatesViewModel>();
            }).Build();

        /// <summary>
        /// Gets services.
        /// </summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// Se déclenche au démarrage de l'application.
        /// Initialise le Host et démarre les services.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            // Empêcher la fermeture auto de l'app quand LoginWindow se ferme
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _logger.Information("=== Démarrage de ADFlowManager ===");
            _logger.Information("Version: {Version}", Assembly.GetExecutingAssembly().GetName().Version);
            _logger.Information("Utilisateur: {User}@{Machine}", Environment.UserName, Environment.MachineName);

            // Check updates en arrière-plan
            _ = CheckForUpdatesAsync();

            // Logs de diagnostic : Informer des chemins de logs utilisés
            var localLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ADFlowManager",
                "logs");
            _logger.Information("✅ Logs locaux activés : {LocalLogPath}", localLogPath);

            var runtimeLogging = LoadLoggingSettings();
            if (!string.IsNullOrWhiteSpace(runtimeLogging.NetworkLogPath))
                _logger.Information("📋 Logs réseau activés : {NetworkLogPath}", runtimeLogging.NetworkLogPath);
            else
                _logger.Information("📋 Logs réseau : Désactivés");

            // Appliquer la langue sauvegardée dans les settings (ou détecter l'OS)
            try
            {
                string cultureCode;
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ADFlowManager", "settings.json");
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    var langIndex = settings?.General?.LanguageIndex ?? -1;
                    if (langIndex >= 0)
                    {
                        cultureCode = langIndex == 1 ? "en-US" : "fr-FR";
                    }
                    else
                    {
                        // Pas de préférence sauvegardée : détecter la langue OS
                        cultureCode = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "fr"
                            ? "fr-FR" : "en-US";
                    }
                }
                else
                {
                    // Pas de fichier settings : détecter la langue OS
                    cultureCode = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "fr"
                        ? "fr-FR" : "en-US";
                }

                var culture = new System.Globalization.CultureInfo(cultureCode);
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
                System.Globalization.CultureInfo.CurrentUICulture = culture;
                System.Globalization.CultureInfo.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                Extensions.LocalizedExtension.OverrideCulture = culture;
                _logger.Information("Langue appliquée : {Culture} (OS: {OsCulture})",
                    cultureCode, System.Globalization.CultureInfo.InstalledUICulture.Name);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Impossible de charger la langue depuis settings.json");
            }

            // Appliquer la couleur d'accent violet (#8B5CF6) pour le thème Dark
            ApplicationAccentColorManager.Apply(
                Color.FromRgb(0x8B, 0x5C, 0xF6),
                ApplicationTheme.Dark);

            // 1. Afficher LoginWindow en modal AVANT d'ouvrir MainWindow
            LoginWindow loginWindow;
            try
            {
                loginWindow = _host.Services.GetRequiredService<LoginWindow>();
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Erreur critique : impossible de créer LoginWindow");
                MessageBox.Show(
                    $"Erreur au démarrage :\n\n{ex.Message}\n\n{ex.InnerException?.Message}",
                    "ADFlowManager - Erreur fatale",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            bool? loginResult = loginWindow.ShowDialog();

            // 2. Si connexion réussie → démarrer l'application principale
            if (loginResult == true)
            {
                _logger.Information("Connexion réussie, ouverture de l'application principale");
                await _host.StartAsync();
                _logger.Information("Application démarrée avec succès");
            }
            else
            {
                // Connexion annulée ou fenêtre fermée → quitter l'app
                _logger.Information("Connexion annulée, fermeture de l'application");
                Shutdown();
            }
        }

        /// <summary>
        /// Vérifie les mises à jour via GitHub Releases.
        /// Non-bloquant : si la vérification échoue, l'app continue normalement.
        /// </summary>
        private static async Task CheckForUpdatesAsync()
        {
            try
            {
                _logger.Information("🔍 Vérification mises à jour...");
                
                // Log version actuelle
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                _logger.Information("Version actuelle : {CurrentVersion}", currentVersion);

                var updateUrl = "https://github.com/Alex-Bumblebee/ADFlowManager";
                _logger.Information("URL GitHub : {UpdateUrl}", updateUrl);
                
                var source = new GithubSource(updateUrl, null, true);
                var mgr = new UpdateManager(source);

                // Check nouvelle version
                _logger.Information("Interrogation GitHub Releases...");
                var newVersion = await mgr.CheckForUpdatesAsync();

                if (newVersion != null)
                {
                    var newVer = newVersion.TargetFullRelease.Version;
                    _logger.Information("📦 Nouvelle version disponible : {Version}", newVer);

                    // Download update en arrière-plan
                    _logger.Information("Téléchargement de la mise à jour...");
                    await mgr.DownloadUpdatesAsync(newVersion);

                    _logger.Information("✅ Mise à jour téléchargée");

                    var result = MessageBox.Show(
                        $"Nouvelle version {newVer} téléchargée.\n\n" +
                        "Redémarrer maintenant pour installer ?",
                        "Mise à jour disponible",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        _logger.Information("🔄 Redémarrage pour mise à jour...");
                        mgr.ApplyUpdatesAndRestart(newVersion);
                    }
                    else
                    {
                        _logger.Information("⏳ Mise à jour reportée au prochain démarrage");
                    }
                }
                else
                {
                    _logger.Information("✅ Application à jour (aucune nouvelle version détectée)");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "⚠️ Échec vérification mises à jour - Détails: {Message}", ex.Message);
                if (ex.InnerException != null)
                {
                    _logger.Error("Exception interne: {InnerMessage}", ex.InnerException.Message);
                }
            }
        }

        /// <summary>
        /// Se déclenche à la fermeture de l'application.
        /// Arrête proprement le Host et libère les ressources.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            _logger.Information("Arrêt de l'application...");

            await _host.StopAsync();
            _host.Dispose();

            Log.CloseAndFlush(); // Ferme Serilog proprement
        }

        /// <summary>
        /// Gère les exceptions non capturées dans l'application.
        /// Logs l'erreur et empêche le crash complet de l'app.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _logger.Fatal(e.Exception, "Exception non gérée dans l'application");

            MessageBox.Show(
                $"Erreur non gérée :\n\n{e.Exception.Message}\n\n{e.Exception.InnerException?.Message}",
                "ADFlowManager - Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }
    }
}
