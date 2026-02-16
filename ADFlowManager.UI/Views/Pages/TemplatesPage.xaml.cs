using ADFlowManager.UI.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Page de gestion des templates utilisateur (CRUD, import/export).
/// </summary>
public partial class TemplatesPage : INavigableView<TemplatesViewModel>
{
    public TemplatesViewModel ViewModel { get; }

    public TemplatesPage(TemplatesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
