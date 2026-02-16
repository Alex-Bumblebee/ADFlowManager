using ADFlowManager.UI.ViewModels.Dialogs;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Dialog de comparaison de groupes AD entre deux utilisateurs.
/// 3 colonnes : Unique User 1 (bleu), Communs (violet), Unique User 2 (orange).
/// </summary>
public partial class CompareUsersDialog
{
    public CompareUsersDialogViewModel ViewModel { get; }

    public CompareUsersDialog(CompareUsersDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
