using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// Wrapper autour de Computer pour ajouter IsSelected (sélection DataGrid).
/// </summary>
public partial class ComputerViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Computer _computer;

    /// <summary>
    /// Événement déclenché quand IsSelected change.
    /// </summary>
    public event Action? SelectionChanged;

    public ComputerViewModel(Computer computer)
    {
        _computer = computer;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }
}

/// <summary>
/// ViewModel de la page Ordinateurs.
/// Gère la liste, la recherche, la sélection et les actions sur les ordinateurs AD.
/// Utilise le cache SQLite pour éviter de recharger depuis AD à chaque démarrage.
/// </summary>
public partial class ComputersViewModel : ObservableObject
{
    private readonly IComputerService _computerService;
    private readonly ICacheService _cacheService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ComputersViewModel> _logger;

    private List<ComputerViewModel> _allComputers = [];

    [ObservableProperty]
    private ObservableCollection<ComputerViewModel> _computers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ComputerViewModel? _selectedComputer;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingText = "";

    [ObservableProperty]
    private int _computerCount;

    [ObservableProperty]
    private bool _isFromCache;

    public bool HasSelection => SelectedComputer is not null;

    public ComputersViewModel(
        IComputerService computerService,
        ICacheService cacheService,
        ILocalizationService localization,
        ILogger<ComputersViewModel> logger)
    {
        _computerService = computerService;
        _cacheService = cacheService;
        _localization = localization;
        _logger = logger;

        _ = LoadComputersAsync();
    }

    /// <summary>
    /// Charge les ordinateurs : d'abord depuis le cache, puis depuis AD si expiré.
    /// </summary>
    [RelayCommand]
    private async Task LoadComputersAsync()
    {
        try
        {
            IsLoading = true;
            LoadingText = _localization.GetString("Computers_LoadingComputers");
            _allComputers = [];
            Computers = [];

            // 1) Essayer le cache
            var cached = await _cacheService.GetCachedComputersAsync();
            if (cached is { Count: > 0 })
            {
                _allComputers = cached
                    .Select(c => new ComputerViewModel(c))
                    .OrderBy(c => c.Computer.Name)
                    .ToList();

                ApplyFilter();
                IsFromCache = true;
                _logger.LogInformation("{Count} ordinateurs chargés depuis le cache", cached.Count);
                return;
            }

            // 2) Sinon charger depuis AD
            IsFromCache = false;
            var computers = await _computerService.GetComputersAsync();
            var computerList = computers.ToList();

            _allComputers = computerList
                .Select(c => new ComputerViewModel(c))
                .OrderBy(c => c.Computer.Name)
                .ToList();

            ApplyFilter();

            // 3) Mettre en cache pour le prochain démarrage
            await _cacheService.CacheComputersAsync(computerList);

            _logger.LogInformation("{Count} ordinateurs chargés depuis AD et mis en cache", computerList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement ordinateurs");
        }
        finally
        {
            IsLoading = false;
            LoadingText = "";
        }
    }

    /// <summary>
    /// Force le rechargement depuis AD (ignore le cache).
    /// </summary>
    [RelayCommand]
    private async Task ForceRefreshAsync()
    {
        try
        {
            IsLoading = true;
            LoadingText = _localization.GetString("Computers_LoadingComputers");
            _allComputers = [];
            Computers = [];

            var computers = await _computerService.GetComputersAsync();
            var computerList = computers.ToList();

            _allComputers = computerList
                .Select(c => new ComputerViewModel(c))
                .OrderBy(c => c.Computer.Name)
                .ToList();

            ApplyFilter();
            IsFromCache = false;

            await _cacheService.CacheComputersAsync(computerList);

            _logger.LogInformation("{Count} ordinateurs rafraîchis depuis AD", computerList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur rafraîchissement ordinateurs");
        }
        finally
        {
            IsLoading = false;
            LoadingText = "";
        }
    }

    /// <summary>
    /// Filtre les ordinateurs selon le texte de recherche.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allComputers
            : _allComputers.Where(c =>
                c.Computer.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Computer.OperatingSystem.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Computer.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Computer.Location.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Computers = new ObservableCollection<ComputerViewModel>(filtered);
        ComputerCount = filtered.Count;
    }

    /// <summary>
    /// Vérifie le statut en ligne d'un ordinateur.
    /// </summary>
    [RelayCommand]
    private async Task CheckOnlineStatusAsync()
    {
        if (SelectedComputer is null) return;

        try
        {
            var isOnline = await _computerService.IsComputerOnlineAsync(SelectedComputer.Computer.Name);
            SelectedComputer.Computer.IsOnline = isOnline;
            OnPropertyChanged(nameof(SelectedComputer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur vérification statut {Computer}", SelectedComputer.Computer.Name);
        }
    }

    /// <summary>
    /// Vérifie le statut en ligne de tous les ordinateurs.
    /// </summary>
    [RelayCommand]
    private async Task CheckAllOnlineStatusAsync()
    {
        IsLoading = true;
        LoadingText = _localization.GetString("Computers_CheckingStatuses");

        try
        {
            var tasks = _allComputers.Select(async c =>
            {
                c.Computer.IsOnline = await _computerService.IsComputerOnlineAsync(c.Computer.Name);
            });

            await Task.WhenAll(tasks);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur vérification statuts en ligne");
        }
        finally
        {
            IsLoading = false;
            LoadingText = "";
        }
    }
}
