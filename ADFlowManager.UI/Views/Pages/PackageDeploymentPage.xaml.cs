using System.Windows.Input;
using ADFlowManager.Core.Models.Deployment;
using ADFlowManager.UI.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Page de déploiement de packages logiciels.
/// </summary>
public partial class PackageDeploymentPage : INavigableView<PackageDeploymentViewModel>
{
    public PackageDeploymentViewModel ViewModel { get; }

    public PackageDeploymentPage(PackageDeploymentViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    /// <summary>
    /// Clic sur un package dans la liste pour le sélectionner.
    /// </summary>
    private void PackageItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement { DataContext: PackageSelectionItem item })
        {
            item.IsSelected = !item.IsSelected;
        }
    }
}
