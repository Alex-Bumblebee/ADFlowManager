using ADFlowManager.UI.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Tableau de bord principal avec stats AD, activité récente et actions rapides.
/// </summary>
public partial class DashboardPage : INavigableView<DashboardViewModel>
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }
}
