using System.Windows.Input;
using ADFlowManager.UI.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Page de connexion Active Directory.
/// Code-behind minimal - toute la logique est dans LoginViewModel.
/// </summary>
public partial class LoginPage : INavigableView<LoginViewModel>
{
    public LoginViewModel ViewModel { get; }

    public LoginPage(LoginViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    private void Page_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel.LoginCommand.CanExecute(null))
        {
            ViewModel.LoginCommand.Execute(null);
        }
    }
}
