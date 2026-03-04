using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using ADFlowManager.Core.Models.Deployment;
using ADFlowManager.UI.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// Item de sélection ordinateur pour le déploiement.
/// </summary>
public partial class ComputerSelectionItem : ObservableObject
{
    [ObservableProperty]
    private Computer _computer;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Événement déclenché quand IsSelected change.
    /// </summary>
    public event Action? SelectionChanged;

    public ComputerSelectionItem(Computer computer)
    {
        _computer = computer;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }
}

/// <summary>
/// Wrapper autour de DeploymentPackage pour ajouter IsSelected (sélection dans la liste).
/// </summary>
public partial class PackageSelectionItem : ObservableObject
{
    [ObservableProperty]
    private DeploymentPackage _package;

    [ObservableProperty]
    private bool _isSelected;

    public event Action? SelectionChanged;

    public PackageSelectionItem(DeploymentPackage package)
    {
        _package = package;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }
}

/// <summary>
/// ViewModel de la page Déploiement de Packages.
/// Gère les packages, la sélection d'ordinateurs cibles, et le déploiement.
/// Utilise le cache SQLite pour les ordinateurs.
/// </summary>
public partial class PackageDeploymentViewModel : ObservableObject
{
    private readonly IPackageService _packageService;
    private readonly IDeploymentService _deploymentService;
    private readonly IComputerService _computerService;
    private readonly ICacheService _cacheService;
    private readonly IPackageSigningService _signingService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<PackageDeploymentViewModel> _logger;

    private List<PackageSelectionItem> _allPackages = [];

    [ObservableProperty]
    private ObservableCollection<PackageSelectionItem> _packages = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeployCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditPackageCommand))]
    private PackageSelectionItem? _selectedPackage;

    [ObservableProperty]
    private ObservableCollection<ComputerSelectionItem> _computers = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeployCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditPackageCommand))]
    private bool _isDeploying;

    [ObservableProperty]
    private string _deploymentStatus = "";

    [ObservableProperty]
    private int _deploymentProgress;

    [ObservableProperty]
    private bool _isLoadingPackages;

    [ObservableProperty]
    private bool _isLoadingComputers;

    [ObservableProperty]
    private int _selectedComputerCount;

    [ObservableProperty]
    private int _packageCount;

    [ObservableProperty]
    private int _selectedPackageCount;

    [ObservableProperty]
    private string _packageSearchText = "";

    [ObservableProperty]
    private string _computerSearchText = "";

    [ObservableProperty]
    private ObservableCollection<DeploymentResult> _deploymentResults = [];

    private List<ComputerSelectionItem> _allComputers = [];

    public PackageDeploymentViewModel(
        IPackageService packageService,
        IDeploymentService deploymentService,
        IComputerService computerService,
        ICacheService cacheService,
        IPackageSigningService signingService,
        ILocalizationService localization,
        ILogger<PackageDeploymentViewModel> logger)
    {
        _packageService = packageService;
        _deploymentService = deploymentService;
        _computerService = computerService;
        _cacheService = cacheService;
        _signingService = signingService;
        _localization = localization;
        _logger = logger;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadPackagesAsync();
        await LoadComputersAsync();
    }

    /// <summary>
    /// Filtre les packages quand le texte de recherche change.
    /// </summary>
    partial void OnPackageSearchTextChanged(string value)
    {
        ApplyPackageFilter();
    }

    /// <summary>
    /// Filtre les ordinateurs quand le texte de recherche change.
    /// </summary>
    partial void OnComputerSearchTextChanged(string value)
    {
        ApplyComputerFilter();
    }

    private void ApplyComputerFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(ComputerSearchText)
            ? _allComputers
            : _allComputers.Where(c =>
                c.Computer.Name.Contains(ComputerSearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Computer.OperatingSystem.Contains(ComputerSearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Computer.Description.Contains(ComputerSearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Computers = new ObservableCollection<ComputerSelectionItem>(filtered);
    }

    private void ApplyPackageFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(PackageSearchText)
            ? _allPackages
            : _allPackages.Where(p =>
                p.Package.Name.Contains(PackageSearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Package.Category.Contains(PackageSearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Package.Description.Contains(PackageSearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Package.Author.Contains(PackageSearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Package.Version.Contains(PackageSearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Packages = new ObservableCollection<PackageSelectionItem>(filtered);
        PackageCount = filtered.Count;
    }

    /// <summary>
    /// Charge les packages depuis le service (fichiers JSON locaux).
    /// </summary>
    [RelayCommand]
    private async Task LoadPackagesAsync()
    {
        IsLoadingPackages = true;

        try
        {
            var packages = await _packageService.GetPackagesAsync();
            var packageList = packages.ToList();
            _logger.LogInformation("{Count} packages chargés", packageList.Count);

            _allPackages = packageList
                .Select(p =>
                {
                    var item = new PackageSelectionItem(p);
                    item.SelectionChanged += UpdatePackageSelection;
                    return item;
                })
                .OrderBy(p => p.Package.Name)
                .ToList();

            ApplyPackageFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement packages");
        }
        finally
        {
            IsLoadingPackages = false;
        }
    }

    private void UpdatePackageSelection()
    {
        // Màj de SelectedPackage pour le bouton Supprimer (premièr package coché)
        SelectedPackage = Packages.FirstOrDefault(p => p.IsSelected);
        SelectedPackageCount = _allPackages.Count(p => p.IsSelected);
        DeployCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Charge les ordinateurs du domaine (depuis le cache).
    /// </summary>
    [RelayCommand]
    private async Task LoadComputersAsync()
    {
        IsLoadingComputers = true;

        try
        {
            List<Computer> computerList;

            var cached = await _cacheService.GetCachedComputersAsync();
            if (cached is { Count: > 0 })
            {
                computerList = cached;
            }
            else
            {
                var computers = await _computerService.GetComputersAsync();
                computerList = computers.ToList();
                await _cacheService.CacheComputersAsync(computerList);
            }

            _allComputers = computerList
                .Select(c =>
                {
                    var item = new ComputerSelectionItem(c);
                    item.SelectionChanged += UpdateSelectedCount;
                    return item;
                })
                .OrderBy(c => c.Computer.Name)
                .ToList();

            ApplyComputerFilter();

            _logger.LogInformation("{Count} ordinateurs chargés pour déploiement", _allComputers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement ordinateurs");
        }
        finally
        {
            IsLoadingComputers = false;
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedComputerCount = _allComputers.Count(c => c.IsSelected);
        DeployCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Lance le déploiement de tous les packages cochés sur tous les ordinateurs cochés.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private async Task DeployAsync()
    {
        var selectedComputers = _allComputers
            .Where(c => c.IsSelected)
            .Select(c => c.Computer.Name)
            .ToList();

        var selectedPackages = _allPackages
            .Where(p => p.IsSelected)
            .Select(p => p.Package)
            .ToList();

        if (selectedComputers.Count == 0 || selectedPackages.Count == 0)
            return;

        // ===== Confirmation stylishée =====
        var confirmDialog = new DeploymentConfirmDialog(selectedPackages, selectedComputers)
        {
            Owner = Application.Current.MainWindow
        };
        confirmDialog.ShowDialog();
        if (!confirmDialog.Confirmed) return;

        IsDeploying = true;
        DeploymentResults.Clear();

        try
        {
            var progress = new Progress<DeploymentProgress>(p =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DeploymentStatus = $"{p.Stage} - {p.Message}";
                    DeploymentProgress = p.Percentage;
                });
            });

            IEnumerable<DeploymentResult> results;

            if (selectedPackages.Count == 1)
            {
                // Chemin rapide : un seul package (pas de surcharge multi)
                results = await _deploymentService.DeployPackageBatchAsync(
                    selectedComputers,
                    selectedPackages[0],
                    maxParallel: 5,
                    progress: progress);
            }
            else
            {
                results = await _deploymentService.DeployMultiplePackagesBatchAsync(
                    selectedComputers,
                    selectedPackages,
                    maxParallel: 5,
                    progress: progress);
            }

            var resultList = results.ToList();
            foreach (var result in resultList)
                DeploymentResults.Add(result);

            var successCount = resultList.Count(r => r.Success);
            var totalCount = resultList.Count;

            DeploymentStatus = string.Format(_localization.GetString("Packages_DeployCompleted"), successCount, totalCount);
            _logger.LogInformation("Déploiement terminé : {Success}/{Total} réussis", successCount, totalCount);

            // ===== Afficher le dialog de résultats détaillé =====
            var resultsDialog = new DeploymentResultsDialog(resultList, selectedPackages)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            resultsDialog.ShowDialog();

            // Appliquer les mises à jour de timeout demandées par l'utilisateur
            foreach (var (pkgId, newTimeout) in resultsDialog.TimeoutUpdates)
            {
                var pkg = selectedPackages.FirstOrDefault(p => p.Id == pkgId);
                if (pkg == null) continue;

                var oldTimeout = pkg.Installer.TimeoutSeconds;
                pkg.Installer.TimeoutSeconds = newTimeout;
                await _packageService.UpdatePackageAsync(pkg);
                _logger.LogInformation(
                    "Timeout du package {Name} mis à jour : {Old}s → {New}s",
                    pkg.Name, oldTimeout, newTimeout);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur déploiement batch");
            DeploymentStatus = $"{_localization.GetString("Common_Error")} : {ex.Message}";

            MessageBox.Show(
                string.Format(_localization.GetString("Packages_DeployError"), ex.Message),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsDeploying = false;
            DeploymentProgress = 0;
        }
    }

    private bool CanDeploy() => !IsDeploying && _allPackages.Any(p => p.IsSelected) && _allComputers.Any(c => c.IsSelected);

    /// <summary>
    /// Sélectionne tous les ordinateurs.
    /// </summary>
    [RelayCommand]
    private void SelectAllComputers()
    {
        foreach (var computer in _allComputers)
        {
            computer.IsSelected = true;
        }
    }

    /// <summary>
    /// Désélectionne tous les ordinateurs.
    /// </summary>
    [RelayCommand]
    private void DeselectAllComputers()
    {
        foreach (var computer in _allComputers)
        {
            computer.IsSelected = false;
        }
    }

    /// <summary>
    /// Supprime un package.
    /// </summary>
    [RelayCommand]
    private async Task DeletePackageAsync()
    {
        if (SelectedPackage == null) return;

        var result = MessageBox.Show(
            string.Format(_localization.GetString("Packages_DeleteConfirm"), SelectedPackage.Package.Name),
            _localization.GetString("Common_Confirm"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _packageService.DeletePackageAsync(SelectedPackage.Package.Id);

            // Reload in delete
            var packagesAfterDelete = (await _packageService.GetPackagesAsync()).ToList();

            _allPackages = packagesAfterDelete
                .Select(p =>
                {
                    var item = new PackageSelectionItem(p);
                    item.SelectionChanged += UpdatePackageSelection;
                    return item;
                })
                .OrderBy(p => p.Package.Name)
                .ToList();

            SelectedPackage = null;
            ApplyPackageFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur suppression package");
            MessageBox.Show($"{_localization.GetString("Common_Error")} : {ex.Message}", _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Ouvre le dialog de création d'un package et persiste le résultat.
    /// </summary>
    [RelayCommand]
    private async Task CreatePackageAsync()
    {
        var dialog = new CreatePackageDialog
        {
            Owner = Application.Current.MainWindow
        };

        dialog.ShowDialog();

        if (!dialog.Confirmed || dialog.CreatedPackage == null) return;

        try
        {
            // Signer le package si une clé ECDSA est disponible
            if (_signingService.IsSigningKeyAvailable())
            {
                _signingService.SignPackage(dialog.CreatedPackage);
                _logger.LogInformation("Package signé avec ECDSA : {Name} v{Version}",
                    dialog.CreatedPackage.Name, dialog.CreatedPackage.Version);
            }
            else
            {
                _logger.LogWarning("Aucune clé de signature — le package '{Name}' ne sera pas signé.",
                    dialog.CreatedPackage.Name);
            }

            await _packageService.CreatePackageAsync(dialog.CreatedPackage);
            _logger.LogInformation("Package créé : {Name} v{Version}", dialog.CreatedPackage.Name, dialog.CreatedPackage.Version);

            await ReloadPackagesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création package");
            MessageBox.Show(
                $"{_localization.GetString("Common_Error")} : {ex.Message}",
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Ouvre le dialog d'édition du package sélectionné.
    /// Accessible uniquement si l'utilisateur possède la clé de signature
    /// (ou si le package n'était pas encore signé).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditPackageAsync()
    {
        if (SelectedPackage == null) return;

        var pkg = SelectedPackage.Package;

        // Contrôle propriétaire : si le package est signé, il faut la clé pour le modifier
        if (pkg.SignatureStatus == PackageSignatureStatus.Valid && !_signingService.IsSigningKeyAvailable())
        {
            MessageBox.Show(
                _localization.GetString("Packages_EditNoKey"),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var thumbprint = _signingService.IsSigningKeyAvailable()
            ? _signingService.GetSigningKeyThumbprint()
            : null;

        var dialog = new CreatePackageDialog(pkg, thumbprint)
        {
            Owner = Application.Current.MainWindow
        };

        dialog.ShowDialog();

        if (!dialog.Confirmed || dialog.CreatedPackage == null) return;

        try
        {
            // Re-signer si la clé est disponible
            if (_signingService.IsSigningKeyAvailable())
            {
                _signingService.SignPackage(dialog.CreatedPackage);
                _logger.LogInformation("Package re-signé après modification : {Name} v{Version}",
                    dialog.CreatedPackage.Name, dialog.CreatedPackage.Version);
            }
            else
            {
                _logger.LogWarning(
                    "Package modifié sans signature (aucune clé disponible) : {Name}",
                    dialog.CreatedPackage.Name);
            }

            await _packageService.UpdatePackageAsync(dialog.CreatedPackage);
            _logger.LogInformation("Package modifié : {Name} v{Version}",
                dialog.CreatedPackage.Name, dialog.CreatedPackage.Version);

            await ReloadPackagesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur modification package");
            MessageBox.Show(
                $"{_localization.GetString("Common_Error")} : {ex.Message}",
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool CanEdit() => SelectedPackage != null && !IsDeploying;

    /// <summary>
    /// Recharge la liste des packages depuis le service et rafraîchit la vue.
    /// </summary>
    private async Task ReloadPackagesAsync()
    {
        var packages = (await _packageService.GetPackagesAsync()).ToList();
        _allPackages = packages
            .Select(p =>
            {
                var item = new PackageSelectionItem(p);
                item.SelectionChanged += UpdatePackageSelection;
                return item;
            })
            .OrderBy(p => p.Package.Name)
            .ToList();
        ApplyPackageFilter();
    }
}
