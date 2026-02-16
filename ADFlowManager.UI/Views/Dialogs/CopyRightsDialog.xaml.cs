using ADFlowManager.UI.ViewModels.Dialogs;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Dialog de copie de droits entre utilisateurs.
/// Recherche un utilisateur source, affiche ses groupes, copie vers la cible.
/// </summary>
public partial class CopyRightsDialog
{
    public CopyRightsDialogViewModel ViewModel { get; }

    /// <summary>
    /// Indique si l'utilisateur a confirmé la copie.
    /// </summary>
    public bool Confirmed { get; private set; }

    public CopyRightsDialog(CopyRightsDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSourceUser is null) return;
        Confirmed = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
