using System.Collections.ObjectModel;
using System.Windows;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// ViewModel de la page Templates.
/// CRUD complet : liste, import, export, suppression.
/// </summary>
public partial class TemplatesViewModel : ObservableObject
{
    private readonly ITemplateService _templateService;
    private readonly ILogger<TemplatesViewModel> _logger;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private ObservableCollection<UserTemplate> _templates = [];

    [ObservableProperty]
    private bool _hasNoTemplates = true;

    [ObservableProperty]
    private bool _isLoading;

    public TemplatesViewModel(
        ITemplateService templateService,
        ILogger<TemplatesViewModel> logger,
        ILocalizationService localization)
    {
        _templateService = templateService;
        _logger = logger;
        _localization = localization;

        _ = LoadTemplatesAsync();
    }

    private async Task LoadTemplatesAsync()
    {
        try
        {
            IsLoading = true;

            var templates = await _templateService.GetAllTemplatesAsync();

            Templates.Clear();
            foreach (var template in templates)
            {
                Templates.Add(template);
            }

            HasNoTemplates = Templates.Count == 0;

            _logger.LogInformation("{Count} templates chargés", templates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement templates");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadTemplatesAsync();
    }

    [RelayCommand]
    private async Task ExportTemplateAsync(UserTemplate? template)
    {
        if (template is null) return;

        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Template JSON (*.json)|*.json",
                FileName = $"Template-{template.Name}.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            await _templateService.ExportTemplateAsync(template, dialog.FileName);

            MessageBox.Show(
                string.Format(_localization.GetString("Settings_ExportSuccess"), dialog.FileName),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur export template {Name}", template.Name);
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteTemplateAsync(UserTemplate? template)
    {
        if (template is null) return;

        try
        {
            var result = MessageBox.Show(
                string.Format(_localization.GetString("Templates_ConfirmDelete"), template.Name),
                _localization.GetString("Common_Confirm"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            await _templateService.DeleteTemplateAsync(template.Id);
            await LoadTemplatesAsync();

            _logger.LogInformation("Template supprimé : {Name}", template.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur suppression template {Name}", template.Name);
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ImportTemplateAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Template JSON (*.json)|*.json",
                Title = _localization.GetString("Templates_ImportTitle")
            };

            if (dialog.ShowDialog() != true)
                return;

            await _templateService.ImportTemplateAsync(dialog.FileName);
            await LoadTemplatesAsync();

            MessageBox.Show(_localization.GetString("Msg_TemplateImported"), _localization.GetString("Common_Success"), MessageBoxButton.OK, MessageBoxImage.Information);

            _logger.LogInformation("Template importé depuis {Path}", dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur import template");
            MessageBox.Show(string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message), _localization.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
