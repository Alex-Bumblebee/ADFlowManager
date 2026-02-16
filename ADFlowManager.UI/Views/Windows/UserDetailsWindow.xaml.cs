using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using ADFlowManager.UI.ViewModels.Windows;
using ADFlowManager.UI.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.Views.Windows;

/// <summary>
/// Fenêtre modale de détails/édition d'un utilisateur AD.
/// Mode consultation par défaut, avec switch pour activer l'édition.
/// </summary>
public partial class UserDetailsWindow
{
    public UserDetailsViewModel ViewModel { get; }

    public UserDetailsWindow(UserDetailsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);

        var result = ViewModel.LastSaveResult;
        if (result is null) return;

        if (result.Success)
        {
            if (result.HasChanges)
            {
                var summary = string.Join("\n", result.Changes.Select(c => $"  • {c}"));
                System.Windows.MessageBox.Show(
                    $"Modifications enregistrées pour {ViewModel.DisplayName} :\n\n{summary}",
                    "Sauvegarde réussie",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Aucune modification détectée pour {ViewModel.DisplayName}.",
                    "Aucun changement",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            Close();
        }
        else
        {
            System.Windows.MessageBox.Show(
                $"Erreur lors de la sauvegarde :\n\n{result.ErrorMessage}",
                "Erreur",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GroupSearchResult_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Group group)
        {
            ViewModel.SelectGroup(group);
        }
    }

    private async void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = App.Services.GetRequiredService<ResetPasswordDialog>();
        dialog.ViewModel.Initialize(ViewModel.UserName, ViewModel.DisplayName);
        dialog.Owner = this;
        dialog.ShowDialog();

        if (dialog.Confirmed)
        {
            try
            {
                var adService = App.Services.GetRequiredService<IActiveDirectoryService>();
                await adService.ResetPasswordAsync(
                    ViewModel.UserName,
                    dialog.ViewModel.NewPassword,
                    dialog.ViewModel.MustChangePasswordNextLogon);

                var localization = App.Services.GetRequiredService<ILocalizationService>();
                System.Windows.MessageBox.Show(
                    string.Format(localization.GetString("ResetPassword_Success"), ViewModel.DisplayName),
                    localization.GetString("Common_Success"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var logger = App.Services.GetRequiredService<ILogger<UserDetailsWindow>>();
                logger.LogError(ex, "❌ Erreur reset mot de passe pour {User}", ViewModel.UserName);

                var localization = App.Services.GetRequiredService<ILocalizationService>();
                System.Windows.MessageBox.Show(
                    string.Format(localization.GetString("Common_ErrorFormat"), ex.Message),
                    localization.GetString("Common_Error"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
