using System.ComponentModel;
using ADFlowManager.UI.ViewModels.Dialogs;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Dialog de réinitialisation de mot de passe.
/// PasswordChanged handlers pour synchroniser les PasswordBox avec le ViewModel.
/// </summary>
public partial class ResetPasswordDialog
{
    public ResetPasswordDialogViewModel ViewModel { get; }

    /// <summary>
    /// Indique si l'utilisateur a confirmé la réinitialisation.
    /// </summary>
    public bool Confirmed { get; private set; }

    private bool _syncingFromViewModel;

    public ResetPasswordDialog(ResetPasswordDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    /// <summary>
    /// Synchronise les PasswordBox quand le ViewModel change le mot de passe (génération).
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingFromViewModel) return;

        if (e.PropertyName == nameof(ViewModel.NewPassword))
        {
            _syncingFromViewModel = true;
            NewPasswordBox.Password = ViewModel.NewPassword;
            _syncingFromViewModel = false;
        }
        else if (e.PropertyName == nameof(ViewModel.ConfirmPassword))
        {
            _syncingFromViewModel = true;
            ConfirmPasswordBox.Password = ViewModel.ConfirmPassword;
            _syncingFromViewModel = false;
        }
    }

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingFromViewModel) return;
        if (sender is Wpf.Ui.Controls.PasswordBox pb)
            ViewModel.NewPassword = pb.Password;
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingFromViewModel) return;
        if (sender is Wpf.Ui.Controls.PasswordBox pb)
            ViewModel.ConfirmPassword = pb.Password;
    }

    private void GeneratedPassword_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.GeneratedPasswordDisplay))
        {
            System.Windows.Clipboard.SetText(ViewModel.GeneratedPasswordDisplay);
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanReset) return;
        Confirmed = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
