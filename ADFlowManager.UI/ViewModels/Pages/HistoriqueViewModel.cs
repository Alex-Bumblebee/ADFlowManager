using System.Collections.ObjectModel;
using System.Windows;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// ViewModel de la page Historique (audit).
/// Affiche les logs d'audit avec filtres, recherche et export CSV.
/// </summary>
public partial class HistoriqueViewModel : ObservableObject
{
    private readonly IAuditService _auditService;
    private readonly ILogger<HistoriqueViewModel> _logger;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private ObservableCollection<AuditLog> _logs = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasNoLogs = true;

    // === Filtres ===
    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _selectedActionIndex;

    [ObservableProperty]
    private int _selectedEntityIndex;

    // === Stats ===
    [ObservableProperty]
    private int _totalLogs;

    [ObservableProperty]
    private int _logsToday;

    [ObservableProperty]
    private int _logsThisWeek;

    /// <summary>
    /// Liste des types d'actions pour le filtre ComboBox.
    /// Index 0 = "Toutes" (pas de filtre).
    /// </summary>
    public List<string> ActionTypes { get; } =
    [
        "Toutes",
        AuditActionType.CreateUser,
        AuditActionType.UpdateUser,
        AuditActionType.DisableUser,
        AuditActionType.EnableUser,
        AuditActionType.ResetPassword,
        AuditActionType.AddUserToGroup,
        AuditActionType.RemoveUserFromGroup,
        AuditActionType.Login
    ];

    /// <summary>
    /// Liste des types d'entités pour le filtre ComboBox.
    /// Index 0 = "Toutes" (pas de filtre).
    /// </summary>
    public List<string> EntityTypes { get; } =
    [
        "Toutes",
        AuditEntityType.User,
        AuditEntityType.Group,
        AuditEntityType.System
    ];

    public HistoriqueViewModel(
        IAuditService auditService,
        ILogger<HistoriqueViewModel> logger,
        ILocalizationService localization)
    {
        _auditService = auditService;
        _logger = logger;
        _localization = localization;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        await LoadLogsAsync();
        await LoadStatsAsync();
    }

    private async Task LoadLogsAsync()
    {
        try
        {
            IsLoading = true;

            string? actionFilter = SelectedActionIndex > 0 ? ActionTypes[SelectedActionIndex] : null;
            string? entityFilter = SelectedEntityIndex > 0 ? EntityTypes[SelectedEntityIndex] : null;
            string? searchFilter = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;

            var logs = await _auditService.GetLogsAsync(
                startDate: StartDate,
                endDate: EndDate?.AddDays(1), // Inclure toute la journée de fin
                username: searchFilter,
                actionType: actionFilter,
                entityType: entityFilter,
                limit: 1000);

            Logs = new ObservableCollection<AuditLog>(logs);
            HasNoLogs = Logs.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement logs audit");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            var stats = await _auditService.GetStatsAsync();
            TotalLogs = stats.TotalLogs;
            LogsToday = stats.LogsToday;
            LogsThisWeek = stats.LogsThisWeek;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur stats audit");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadLogsAsync();
        await LoadStatsAsync();
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        await LoadLogsAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        StartDate = null;
        EndDate = null;
        SearchText = "";
        SelectedActionIndex = 0;
        SelectedEntityIndex = 0;
        await LoadLogsAsync();
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Fichiers CSV (*.csv)|*.csv",
                FileName = $"Audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                Title = _localization.GetString("History_ExportTitle")
            };

            if (dialog.ShowDialog() != true) return;

            await _auditService.ExportToCsvAsync(dialog.FileName, StartDate, EndDate);

            MessageBox.Show(
                string.Format(_localization.GetString("Settings_ExportSuccess"), dialog.FileName),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _logger.LogInformation("Audit exporté CSV : {Path}", dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur export CSV");
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
