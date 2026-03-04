using System.IO;
using System.Text.Json;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models.Deployment;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.Services;

/// <summary>
/// Service de gestion des packages de déploiement.
/// Stockage JSON dans %APPDATA%\ADFlowManager\Packages (local) + chemin réseau optionnel.
/// </summary>
public class PackageService : IPackageService
{
    private readonly ISettingsService _settingsService;
    private readonly IPackageSigningService _signingService;
    private readonly ILogger<PackageService> _logger;
    private readonly string _packagesDirectory;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PackageService(
        ISettingsService settingsService,
        IPackageSigningService signingService,
        ILogger<PackageService> logger)
    {
        _settingsService = settingsService;
        _signingService = signingService;
        _logger = logger;

        // Répertoire packages : %APPDATA%/ADFlowManager/Packages
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _packagesDirectory = Path.Combine(appData, "ADFlowManager", "Packages");

        EnsureDirectoriesExist();
    }

    private void EnsureDirectoriesExist()
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(_packagesDirectory, "Local"));
            _logger.LogDebug("Répertoire packages initialisé : {Path}", _packagesDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création répertoire packages");
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<DeploymentPackage>> GetPackagesAsync()
    {
        var packages = new List<DeploymentPackage>();

        // Charger packages locaux
        var localPackages = await LoadPackagesFromDirectoryAsync(
            Path.Combine(_packagesDirectory, "Local"));
        packages.AddRange(localPackages);

        // Charger packages réseau si chemin configuré
        var networkPath = _settingsService.CurrentSettings.Deployment?.NetworkPackagesPath;
        if (!string.IsNullOrWhiteSpace(networkPath) && Directory.Exists(networkPath))
        {
            try
            {
                var networkPackages = await LoadPackagesFromDirectoryAsync(networkPath);
                packages.AddRange(networkPackages);
                _logger.LogDebug("{Count} packages réseau chargés depuis {Path}", networkPackages.Count, networkPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur chargement packages réseau depuis {Path}", networkPath);
            }
        }

        _logger.LogInformation("{Count} packages chargés au total", packages.Count);
        return packages;
    }

    private async Task<List<DeploymentPackage>> LoadPackagesFromDirectoryAsync(string directory)
    {
        var packages = new List<DeploymentPackage>();

        if (!Directory.Exists(directory))
            return packages;

        var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var package = JsonSerializer.Deserialize<DeploymentPackage>(json, _jsonOptions);
                if (package != null)
                {
                    // VULN-14 : vérifier la signature au chargement
                    package.SignatureStatus = VerifyPackageSignature(package);
                    packages.Add(package);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur chargement package {File}", file);
            }
        }

        return packages;
    }

    /// <summary>
    /// Vérifie la signature ECDSA d'un package au chargement.
    /// </summary>
    private PackageSignatureStatus VerifyPackageSignature(DeploymentPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Signature))
            return PackageSignatureStatus.Unsigned;

        try
        {
            return _signingService.VerifyPackage(package)
                ? PackageSignatureStatus.Valid
                : PackageSignatureStatus.Invalid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur vérification signature pour {Package}", package.Name);
            return PackageSignatureStatus.Invalid;
        }
    }

    /// <inheritdoc/>
    public async Task<DeploymentPackage?> GetPackageByIdAsync(string id)
    {
        var packages = await GetPackagesAsync();
        return packages.FirstOrDefault(p => p.Id == id);
    }

    /// <inheritdoc/>
    public async Task<DeploymentPackage> CreatePackageAsync(DeploymentPackage package)
    {
        package.Id = Guid.NewGuid().ToString();
        package.Created = DateTime.UtcNow;
        package.Updated = DateTime.UtcNow;

        var category = string.IsNullOrWhiteSpace(package.Category) ? "General" : package.Category;

        // VULN-13 : path traversal — reject if category contains path separators or ".."
        var safeCategory = Path.GetFileName(category);
        if (string.IsNullOrWhiteSpace(safeCategory) || safeCategory != category)
            throw new ArgumentException($"Invalid package category: '{category}'. Must be a simple folder name without path separators.");

        var directory = Path.Combine(_packagesDirectory, "Local", safeCategory);
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"{package.Id}.json");
        var json = JsonSerializer.Serialize(package, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        _logger.LogInformation("Package créé : {Name} ({Id})", package.Name, package.Id);
        return package;
    }

    /// <inheritdoc/>
    public async Task UpdatePackageAsync(DeploymentPackage package)
    {
        package.Updated = DateTime.UtcNow;

        // Trouver fichier existant
        var files = Directory.GetFiles(_packagesDirectory, $"{package.Id}.json", SearchOption.AllDirectories);

        if (files.Length == 0)
            throw new FileNotFoundException($"Package {package.Id} introuvable");

        var filePath = files[0];
        var json = JsonSerializer.Serialize(package, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        _logger.LogInformation("Package mis à jour : {Name}", package.Name);
    }

    /// <inheritdoc/>
    public async Task DeletePackageAsync(string id)
    {
        var files = Directory.GetFiles(_packagesDirectory, $"{id}.json", SearchOption.AllDirectories);

        if (files.Length == 0)
            throw new FileNotFoundException($"Package {id} introuvable");

        File.Delete(files[0]);
        _logger.LogInformation("Package supprimé : {Id}", id);

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<DeploymentPackage> ImportPackageAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var package = JsonSerializer.Deserialize<DeploymentPackage>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Fichier JSON invalide");

        return await CreatePackageAsync(package);
    }

    /// <inheritdoc/>
    public async Task ExportPackageAsync(DeploymentPackage package, string filePath)
    {
        var json = JsonSerializer.Serialize(package, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        _logger.LogInformation("Package exporté : {Path}", filePath);
    }
}
