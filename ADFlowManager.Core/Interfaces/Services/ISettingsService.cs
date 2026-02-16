using ADFlowManager.Core.Models;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service de gestion des paramètres application.
/// Persistence JSON dans %APPDATA%\ADFlowManager\settings.json
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Charger settings depuis JSON.
    /// </summary>
    Task<AppSettings> LoadSettingsAsync();

    /// <summary>
    /// Sauvegarder settings vers JSON.
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings);

    /// <summary>
    /// Exporter configuration vers fichier externe.
    /// </summary>
    Task ExportSettingsAsync(string filePath);

    /// <summary>
    /// Importer configuration depuis fichier externe.
    /// </summary>
    Task<AppSettings> ImportSettingsAsync(string filePath);

    /// <summary>
    /// Settings actuels (cached en mémoire).
    /// </summary>
    AppSettings CurrentSettings { get; }
}
