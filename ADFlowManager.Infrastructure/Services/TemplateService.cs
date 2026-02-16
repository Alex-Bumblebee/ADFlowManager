using System.IO;
using System.Text.Json;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.Services;

/// <summary>
/// Service de gestion des templates utilisateur.
/// Stockage JSON : 1 fichier = 1 template, local ou réseau partagé.
/// Filename : {Id}_{SafeName}.json
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TemplateService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TemplateService(
        ISettingsService settingsService,
        ILogger<TemplateService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        EnsureFoldersExist();
    }

    private void EnsureFoldersExist()
    {
        try
        {
            var localPath = _settingsService.CurrentSettings.Templates.LocalFolderPath;
            Directory.CreateDirectory(localPath);

            var networkPath = _settingsService.CurrentSettings.Templates.NetworkFolderPath;
            if (!string.IsNullOrWhiteSpace(networkPath))
            {
                try
                {
                    Directory.CreateDirectory(networkPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Dossier réseau templates inaccessible : {Path}", networkPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création dossiers templates");
        }
    }

    private string GetTemplatesFolderPath()
    {
        var settings = _settingsService.CurrentSettings.Templates;

        if (settings.StorageMode == "Network" && !string.IsNullOrWhiteSpace(settings.NetworkFolderPath))
        {
            if (Directory.Exists(settings.NetworkFolderPath))
            {
                return settings.NetworkFolderPath;
            }

            _logger.LogWarning("Dossier réseau templates inaccessible, fallback local : {Path}", settings.NetworkFolderPath);
        }

        return settings.LocalFolderPath;
    }

    public async Task<List<UserTemplate>> GetAllTemplatesAsync()
    {
        try
        {
            var folderPath = GetTemplatesFolderPath();

            if (!Directory.Exists(folderPath))
            {
                _logger.LogInformation("Aucun dossier templates trouvé : {Path}", folderPath);
                return new List<UserTemplate>();
            }

            var jsonFiles = Directory.GetFiles(folderPath, "*.json");
            var templates = new List<UserTemplate>();

            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    var template = JsonSerializer.Deserialize<UserTemplate>(json, JsonOptions);

                    if (template != null)
                    {
                        templates.Add(template);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erreur lecture template : {File}", file);
                }
            }

            _logger.LogInformation("{Count} templates chargés depuis {Path}", templates.Count, folderPath);

            return templates.OrderBy(t => t.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur récupération templates");
            return new List<UserTemplate>();
        }
    }

    public async Task<UserTemplate?> GetTemplateByIdAsync(string id)
    {
        try
        {
            var templates = await GetAllTemplatesAsync().ConfigureAwait(false);
            return templates.FirstOrDefault(t => t.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur récupération template {Id}", id);
            return null;
        }
    }

    public async Task<UserTemplate> SaveTemplateAsync(UserTemplate template)
    {
        try
        {
            var folderPath = GetTemplatesFolderPath();
            Directory.CreateDirectory(folderPath);

            template.ModifiedAt = DateTime.Now;

            // Supprimer l'ancien fichier si le template existe déjà (nom peut avoir changé)
            var existingFiles = Directory.GetFiles(folderPath, $"{template.Id}_*.json");
            foreach (var f in existingFiles)
            {
                File.Delete(f);
            }

            var safeName = string.Join("_", template.Name.Split(Path.GetInvalidFileNameChars()));
            var filePath = Path.Combine(folderPath, $"{template.Id}_{safeName}.json");

            var json = JsonSerializer.Serialize(template, JsonOptions);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            _logger.LogInformation("Template sauvegardé : {Name} ({Path})", template.Name, filePath);

            return template;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur sauvegarde template {Name}", template.Name);
            throw;
        }
    }

    public Task DeleteTemplateAsync(string id)
    {
        try
        {
            var folderPath = GetTemplatesFolderPath();

            var files = Directory.GetFiles(folderPath, $"{id}_*.json");

            if (files.Length == 0)
            {
                _logger.LogWarning("Template non trouvé pour suppression : {Id}", id);
                return Task.CompletedTask;
            }

            foreach (var file in files)
            {
                File.Delete(file);
                _logger.LogInformation("Template supprimé : {File}", file);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur suppression template {Id}", id);
            throw;
        }
    }

    public async Task ExportTemplateAsync(UserTemplate template, string filePath)
    {
        try
        {
            var json = JsonSerializer.Serialize(template, JsonOptions);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            _logger.LogInformation("Template exporté : {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur export template vers {Path}", filePath);
            throw;
        }
    }

    public async Task<UserTemplate> ImportTemplateAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Fichier introuvable : {filePath}");
            }

            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var template = JsonSerializer.Deserialize<UserTemplate>(json, JsonOptions);

            if (template == null)
            {
                throw new InvalidDataException("Fichier template invalide");
            }

            // Nouveau ID pour éviter conflit
            template.Id = Guid.NewGuid().ToString();
            template.CreatedAt = DateTime.Now;
            template.ModifiedAt = DateTime.Now;
            template.CreatedBy = Environment.UserName;

            // Sauvegarder dans dossier templates
            await SaveTemplateAsync(template).ConfigureAwait(false);

            _logger.LogInformation("Template importé : {Name}", template.Name);

            return template;
        }
        catch (Exception ex) when (ex is not FileNotFoundException and not InvalidDataException)
        {
            _logger.LogError(ex, "Erreur import template depuis {Path}", filePath);
            throw;
        }
    }
}
