using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.UI.ViewModels.Pages;
using ADFlowManager.UI.Views.Dialogs;
using ADFlowManager.UI.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Page de gestion des utilisateurs Active Directory.
/// </summary>
public partial class UsersPage : INavigableView<UsersViewModel>
{
    public UsersViewModel ViewModel { get; }

    public UsersPage(UsersViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    /// <summary>
    /// Charge un utilisateur LIVE depuis AD (pas depuis le cache).
    /// </summary>
    private async Task<ADFlowManager.Core.Models.User?> LoadUserLiveAsync(string userName)
    {
        try
        {
            var adService = App.Services.GetRequiredService<IActiveDirectoryService>();
            return await adService.GetUserByUsernameAsync(userName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Double-clic sur une ligne du DataGrid → ouvre la fenêtre de détails utilisateur (LIVE).
    /// </summary>
    private async void UsersDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Vérifier qu'on a bien cliqué sur une DataGridRow (pas un header)
        var dep = (DependencyObject)e.OriginalSource;
        while (dep is not null and not DataGridRow and not DataGridColumnHeader)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not DataGridRow) return;
        if (ViewModel.SelectedUser?.User is null) return;

        var liveUser = await LoadUserLiveAsync(ViewModel.SelectedUser.User.UserName);
        if (liveUser is null) return;

        var window = App.Services.GetRequiredService<UserDetailsWindow>();
        window.ViewModel.LoadUser(liveUser);
        window.Owner = Window.GetWindow(this);
        window.ShowDialog();
    }

    /// <summary>
    /// Bouton Modifier l'utilisateur → ouvre UserDetailsWindow en mode édition (LIVE).
    /// </summary>
    private async void EditUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedUser?.User is null) return;

        var liveUser = await LoadUserLiveAsync(ViewModel.SelectedUser.User.UserName);
        if (liveUser is null) return;

        var window = App.Services.GetRequiredService<UserDetailsWindow>();
        window.ViewModel.LoadUser(liveUser, startInEditMode: true);
        window.Owner = Window.GetWindow(this);
        window.ShowDialog();
    }

    /// <summary>
    /// Bouton Gérer les groupes → ouvre UserDetailsWindow en mode édition sur l'onglet Groupes (LIVE).
    /// </summary>
    private async void ManageGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedUser?.User is null) return;

        var liveUser = await LoadUserLiveAsync(ViewModel.SelectedUser.User.UserName);
        if (liveUser is null) return;

        var window = App.Services.GetRequiredService<UserDetailsWindow>();
        window.ViewModel.LoadUser(liveUser, startInEditMode: true, initialTabIndex: 3);
        window.Owner = Window.GetWindow(this);
        window.ShowDialog();
    }

    /// <summary>
    /// Bouton Désactiver/Activer un seul compte → confirmation puis appel AD.
    /// Si DisabledUserOU est configuré, déplace l'utilisateur dans l'OU correspondante.
    /// </summary>
    private async void DisableUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedUser?.User is null) return;

        var user = ViewModel.SelectedUser.User;
        var action = user.IsEnabled ? "désactiver" : "activer";
        var result = System.Windows.MessageBox.Show(
            $"Voulez-vous vraiment {action} le compte de {user.DisplayName} ({user.UserName}) ?",
            "Confirmation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        var adService = App.Services.GetRequiredService<ADFlowManager.Core.Interfaces.Services.IActiveDirectoryService>();
        var cacheService = App.Services.GetRequiredService<ADFlowManager.Core.Interfaces.Services.ICacheService>();
        var settingsService = App.Services.GetRequiredService<ADFlowManager.Core.Interfaces.Services.ISettingsService>();
        var adSettings = settingsService.CurrentSettings.ActiveDirectory;

        if (user.IsEnabled)
        {
            await ViewModel.DisableUserAsync(user);

            // Déplacer vers l'OU de désactivation si configurée
            if (!string.IsNullOrWhiteSpace(adSettings.DisabledUserOU))
            {
                try
                {
                    await adService.MoveUserToOUAsync(user.UserName, adSettings.DisabledUserOU);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Compte désactivé mais erreur de déplacement vers l'OU :\n{ex.Message}",
                        "Avertissement",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
        }
        else
        {
            // Activer le compte
            await adService.SetUserEnabledAsync(user.UserName, true);
            user.IsEnabled = true;

            // Remettre dans l'OU de création si configurée
            if (!string.IsNullOrWhiteSpace(adSettings.DefaultUserOU))
            {
                try
                {
                    await adService.MoveUserToOUAsync(user.UserName, adSettings.DefaultUserOU);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Compte activé mais erreur de déplacement vers l'OU :\n{ex.Message}",
                        "Avertissement",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }

            await cacheService.ClearCacheAsync();
        }
    }

    /// <summary>
    /// Bouton Désactiver les comptes (bulk) → confirmation puis désactivation un par un.
    /// </summary>
    private async void BulkDisableButton_Click(object sender, RoutedEventArgs e)
    {
        var checkedUsers = ViewModel.GetCheckedUsers();
        var activeUsers = checkedUsers.Where(u => u.IsEnabled).ToList();

        if (activeUsers.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Aucun compte actif parmi la sélection.",
                "Information",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Voulez-vous vraiment désactiver {activeUsers.Count} compte(s) ?\n\n" +
            string.Join("\n", activeUsers.Select(u => $"  • {u.DisplayName} ({u.UserName})")),
            "Confirmation — Désactivation en masse",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        await ViewModel.BulkDisableAsync();

        System.Windows.MessageBox.Show(
            $"{activeUsers.Count} compte(s) désactivé(s) avec succès.",
            "Terminé",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    /// <summary>
    /// Bouton Ajouter aux groupes (bulk) → autosuggest groupes, puis ajoute tous les utilisateurs cochés.
    /// </summary>
    private async void BulkAddToGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var checkedUsers = ViewModel.GetCheckedUsers();
        if (checkedUsers.Count == 0) return;

        var dialog = new Dialogs.GroupSearchDialog(
            $"Ajouter {checkedUsers.Count} utilisateur(s) aux groupes");
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() != true) return;

        var selectedGroups = dialog.SelectedGroups;
        if (selectedGroups.Count == 0) return;

        var groupNames = selectedGroups.Select(g => g.GroupName).ToList();

        var confirmMsg = $"Ajouter {checkedUsers.Count} utilisateur(s) à {groupNames.Count} groupe(s) ?\n\n" +
            "Utilisateurs :\n" +
            string.Join("\n", checkedUsers.Select(u => $"  • {u.DisplayName} ({u.UserName})")) +
            "\n\nGroupes :\n" +
            string.Join("\n", groupNames.Select(g => $"  • {g}"));

        var confirm = System.Windows.MessageBox.Show(
            confirmMsg,
            "Confirmation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        var adService = App.Services.GetRequiredService<IActiveDirectoryService>();
        var cacheService = App.Services.GetRequiredService<ICacheService>();
        int success = 0, errors = 0;

        foreach (var user in checkedUsers)
        {
            foreach (var groupName in groupNames)
            {
                try
                {
                    await adService.AddUserToGroupAsync(user.UserName, groupName);
                    success++;
                }
                catch
                {
                    errors++;
                }
            }
        }

        await cacheService.ClearCacheAsync();

        var resultMsg = $"{success} ajout(s) réussi(s).";
        if (errors > 0) resultMsg += $"\n{errors} erreur(s).";

        System.Windows.MessageBox.Show(
            resultMsg,
            "Terminé",
            System.Windows.MessageBoxButton.OK,
            errors > 0 ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
    }

    /// <summary>
    /// Bouton Comparer → ouvre le dialog de comparaison entre les 2 utilisateurs cochés (LIVE).
    /// </summary>
    private async void CompareUsersButton_Click(object sender, RoutedEventArgs e)
    {
        var pair = ViewModel.GetSelectedUsersForCompare();
        if (pair is null) return;

        var liveUser1 = await LoadUserLiveAsync(pair.Value.user1.UserName);
        var liveUser2 = await LoadUserLiveAsync(pair.Value.user2.UserName);

        if (liveUser1 is null || liveUser2 is null)
        {
            System.Windows.MessageBox.Show(
                "Impossible de charger les utilisateurs depuis AD.",
                "Erreur",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var dialog = App.Services.GetRequiredService<CompareUsersDialog>();
        dialog.ViewModel.Initialize(liveUser1, liveUser2);
        dialog.Owner = Window.GetWindow(this);
        dialog.ShowDialog();
    }
}
