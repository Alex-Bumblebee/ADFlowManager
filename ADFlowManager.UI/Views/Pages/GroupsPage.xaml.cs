using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using ADFlowManager.UI.ViewModels.Pages;
using ADFlowManager.UI.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Page de gestion des groupes Active Directory.
/// </summary>
public partial class GroupsPage : INavigableView<GroupsViewModel>
{
    public GroupsViewModel ViewModel { get; }

    public GroupsPage(GroupsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Vérifier qu'on a bien cliqué sur une DataGridRow (pas un header)
        var dep = (DependencyObject)e.OriginalSource;
        while (dep is not null and not DataGridRow and not DataGridColumnHeader)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not DataGridRow) return;

        // Double-click on a member row inside the details panel would hit this too,
        // but we only care about the DataGrid rows
        if (ViewModel.SelectedGroup is null) return;
    }

    /// <summary>
    /// Bouton Ajouter des membres (single group) → recherche utilisateurs puis ajout AD.
    /// </summary>
    private async void AddMembersButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is null) return;

        var groupNames = new List<string> { ViewModel.SelectedGroup.GroupName };
        await PromptAndAddMembersAsync(groupNames);
    }

    /// <summary>
    /// Bouton Ajouter des membres aux groupes (multi-sélection) → recherche utilisateurs puis ajout AD sur tous les groupes cochés.
    /// </summary>
    private async void BulkAddMembersButton_Click(object sender, RoutedEventArgs e)
    {
        var checkedGroups = ViewModel.GetCheckedGroups();
        if (checkedGroups.Count == 0) return;

        var groupNames = checkedGroups.Select(g => g.GroupName).ToList();
        await PromptAndAddMembersAsync(groupNames);
    }

    /// <summary>
    /// Dialogue de recherche d'utilisateurs (autosuggest) et ajout aux groupes spécifiés.
    /// </summary>
    private async Task PromptAndAddMembersAsync(List<string> groupNames)
    {
        var groupLabel = groupNames.Count == 1
            ? groupNames[0]
            : $"{groupNames.Count} groupes";

        var dialog = new Dialogs.UserSearchDialog($"Ajouter des membres à {groupLabel}");
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() != true) return;

        var selectedUsers = dialog.SelectedUsers;
        if (selectedUsers.Count == 0) return;

        // Confirm
        var confirmMsg = $"Ajouter {selectedUsers.Count} utilisateur(s) à {groupLabel} ?\n\n" +
            string.Join("\n", selectedUsers.Select(u => $"  • {u.DisplayName} ({u.UserName})")) +
            "\n\nGroupe(s) :\n" +
            string.Join("\n", groupNames.Select(g => $"  • {g}"));

        var confirmResult = System.Windows.MessageBox.Show(
            confirmMsg,
            "Confirmation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirmResult != System.Windows.MessageBoxResult.Yes) return;

        await ViewModel.AddMembersToGroupsAsync(groupNames, selectedUsers);

        System.Windows.MessageBox.Show(
            $"{selectedUsers.Count} utilisateur(s) ajouté(s) à {groupLabel}.",
            "Terminé",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    /// <summary>
    /// Parcourir les OUs pour la création de groupe.
    /// </summary>
    private async void BrowseGroupOU_Click(object sender, RoutedEventArgs e)
    {
        var ous = await ViewModel.GetAvailableOUsAsync();
        if (ous.Count == 0)
        {
            System.Windows.MessageBox.Show("Aucune OU disponible.", "Information",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var window = new System.Windows.Window
        {
            Title = "Sélectionner l'OU de destination",
            Width = 600,
            Height = 450,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
            ResizeMode = System.Windows.ResizeMode.CanResize
        };

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Auto) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Auto) });

        var searchBox = new System.Windows.Controls.TextBox
        {
            Margin = new System.Windows.Thickness(12),
            Padding = new System.Windows.Thickness(8, 6, 8, 6),
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30)),
        };
        System.Windows.Controls.Grid.SetRow(searchBox, 0);
        grid.Children.Add(searchBox);

        var listBox = new System.Windows.Controls.ListBox
        {
            Margin = new System.Windows.Thickness(12, 0, 12, 12),
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30)),
            FontSize = 13
        };

        var sortedOUs = ous.OrderBy(o => o.Path).ToList();

        void RefreshList(string filter)
        {
            listBox.Items.Clear();
            var filtered = string.IsNullOrWhiteSpace(filter)
                ? sortedOUs
                : sortedOUs.Where(o =>
                    o.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    o.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var ou in filtered)
            {
                listBox.Items.Add(new System.Windows.Controls.ListBoxItem
                {
                    Content = $"{ou.Name}  —  {ou.Path}",
                    Tag = ou.Path,
                    Foreground = System.Windows.Media.Brushes.White,
                    Padding = new System.Windows.Thickness(8, 6, 8, 6)
                });
            }
        }

        RefreshList("");
        searchBox.TextChanged += (s, ev) => RefreshList(searchBox.Text);

        System.Windows.Controls.Grid.SetRow(listBox, 1);
        grid.Children.Add(listBox);

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new System.Windows.Thickness(12)
        };

        string? result = null;

        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "Annuler",
            Padding = new System.Windows.Thickness(20, 6, 20, 6),
            Margin = new System.Windows.Thickness(0, 0, 8, 0)
        };
        cancelBtn.Click += (s, ev) => window.Close();
        btnPanel.Children.Add(cancelBtn);

        var okBtn = new System.Windows.Controls.Button
        {
            Content = "Sélectionner",
            Padding = new System.Windows.Thickness(20, 6, 20, 6)
        };
        okBtn.Click += (s, ev) =>
        {
            if (listBox.SelectedItem is System.Windows.Controls.ListBoxItem selected)
            {
                result = selected.Tag?.ToString();
                window.Close();
            }
        };
        btnPanel.Children.Add(okBtn);

        listBox.MouseDoubleClick += (s, ev) =>
        {
            if (listBox.SelectedItem is System.Windows.Controls.ListBoxItem selected)
            {
                result = selected.Tag?.ToString();
                window.Close();
            }
        };

        System.Windows.Controls.Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        window.Content = grid;
        window.ShowDialog();

        if (result != null)
            ViewModel.SetNewGroupOU(result);
    }

    /// <summary>
    /// Retirer un membre du groupe sélectionné.
    /// </summary>
    private async void RemoveMemberButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is null) return;
        if (sender is not FrameworkElement fe) return;

        var user = fe.DataContext as User;
        if (user is null && fe is System.Windows.Controls.Button btn)
            user = btn.CommandParameter as User;

        if (user is null) return;

        var result = System.Windows.MessageBox.Show(
            $"Retirer {user.DisplayName} ({user.UserName}) du groupe {ViewModel.SelectedGroup.GroupName} ?",
            "Confirmation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        await ViewModel.RemoveMemberFromGroupAsync(ViewModel.SelectedGroup.GroupName, user);
    }
}
