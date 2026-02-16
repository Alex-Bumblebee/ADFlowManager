using System.Windows.Input;
using ADFlowManager.UI.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Page de paramètres avec navigation sidebar (5 onglets).
/// </summary>
public partial class SettingsPage : INavigableView<SettingsViewModel>
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    /// <summary>
    /// Gère le clic sur un élément de la sidebar via la propriété Tag (index de l'onglet).
    /// </summary>
    private void SidebarItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out var tab))
            ViewModel.SelectedTab = tab;
    }
}
