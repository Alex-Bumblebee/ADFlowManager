using System.Text.Json;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.Services;

/// <summary>
/// Service de persistence des paramètres application en JSON.
/// Fichier : %APPDATA%\ADFlowManager\settings.json
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly string _settingsPath;
    private AppSettings _currentSettings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings CurrentSettings => _currentSettings;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDir = Path.Combine(appData, "ADFlowManager");
        Directory.CreateDirectory(appDir);

        _settingsPath = Path.Combine(appDir, "settings.json");

        // Charger settings au démarrage (synchrone pour éviter deadlock WPF DispatcherSynchronizationContext)
        _currentSettings = LoadSettingsSync();
    }

    private AppSettings LoadSettingsSync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger.LogInformation("Settings non trouvés, création par défaut");
                var defaultSettings = new AppSettings();
                var json = JsonSerializer.Serialize(defaultSettings, JsonOptions);
                File.WriteAllText(_settingsPath, json);
                return defaultSettings;
            }

            var content = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(content, JsonOptions);

            if (settings == null)
            {
                _logger.LogWarning("Settings invalides, reset par défaut");
                return new AppSettings();
            }

            _logger.LogInformation("Settings chargés : {Path}", _settingsPath);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement settings");
            return new AppSettings();
        }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger.LogInformation("Settings non trouvés, création par défaut");
                var defaultSettings = new AppSettings();
                await SaveSettingsAsync(defaultSettings).ConfigureAwait(false);
                return defaultSettings;
            }

            var json = await File.ReadAllTextAsync(_settingsPath).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (settings == null)
            {
                _logger.LogWarning("Settings invalides, reset par défaut");
                return new AppSettings();
            }

            _logger.LogInformation("Settings chargés : {Path}", _settingsPath);
            _currentSettings = settings;
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement settings");
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json).ConfigureAwait(false);

            _currentSettings = settings;

            _logger.LogInformation("Settings sauvegardés : {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur sauvegarde settings");
            throw;
        }
    }

    public async Task ExportSettingsAsync(string filePath)
    {
        try
        {
            var json = JsonSerializer.Serialize(_currentSettings, JsonOptions);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            _logger.LogInformation("Configuration exportée : {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur export configuration");
            throw;
        }
    }

    public async Task<AppSettings> ImportSettingsAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Fichier introuvable : {filePath}");

            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (settings == null)
                throw new InvalidDataException("Fichier configuration invalide");

            if (settings.ConfigVersion > _currentSettings.ConfigVersion)
            {
                _logger.LogWarning("Version config supérieure détectée : {Version}", settings.ConfigVersion);
                throw new InvalidOperationException(
                    $"Configuration version {settings.ConfigVersion} non supportée. " +
                    $"Version actuelle : {_currentSettings.ConfigVersion}");
            }

            await SaveSettingsAsync(settings).ConfigureAwait(false);

            _logger.LogInformation("Configuration importée : {Path}", filePath);

            return settings;
        }
        catch (Exception ex) when (ex is not FileNotFoundException and not InvalidDataException and not InvalidOperationException)
        {
            _logger.LogError(ex, "Erreur import configuration");
            throw;
        }
    }
}
