using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ADFlowManager.UI.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Page de gestion des ordinateurs Active Directory.
/// </summary>
public partial class ComputersPage : INavigableView<ComputersViewModel>
{
    public ComputersViewModel ViewModel { get; }

    public ComputersPage(ComputersViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    /// <summary>
    /// Double-clic sur une ligne pour ouvrir les détails.
    /// </summary>
    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Vérifier qu'on a bien cliqué sur une DataGridRow (pas un header)
        var dep = (DependencyObject)e.OriginalSource;
        while (dep is not null and not DataGridRow and not DataGridColumnHeader)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not DataGridRow) return;
        OpenDetailsWindow();
    }

    /// <summary>
    /// Bouton "Ouvrir les détails" dans le panneau latéral.
    /// </summary>
    private void OpenDetailsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        OpenDetailsWindow();
    }

    private void OpenDetailsWindow()
    {
        if (ViewModel.SelectedComputer is null) return;

        var detailsWindow = new Windows.ComputerDetailsWindow(ViewModel.SelectedComputer.Computer);
        detailsWindow.Owner = Window.GetWindow(this);
        detailsWindow.ShowDialog();
    }
}
