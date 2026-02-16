using ADFlowManager.UI.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Page Historique (audit) - affiche les logs d'actions AD.
/// </summary>
public partial class HistoriquePage : INavigableView<HistoriqueViewModel>
{
    public HistoriqueViewModel ViewModel { get; }

    public HistoriquePage(HistoriqueViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}
