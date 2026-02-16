using ADFlowManager.UI.ViewModels.Dialogs;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Dialog de copie depuis un utilisateur existant.
/// Permet de rechercher un user source et copier ses données organisationnelles + groupes.
/// </summary>
public partial class CopyFromUserDialog
{
    public CopyFromUserDialogViewModel ViewModel { get; }

    public CopyFromUserDialog(CopyFromUserDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();

        // Passer la référence window au ViewModel pour fermeture
        ViewModel.DialogWindow = this;
    }
}
