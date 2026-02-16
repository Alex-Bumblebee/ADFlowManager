using System.Reflection;
using ADFlowManager.UI.ViewModels.Pages;
using ADFlowManager.UI.Views.Pages;

namespace ADFlowManager.UI.Views.Windows;

/// <summary>
/// Fenêtre de connexion séparée affichée en modal avant MainWindow.
/// Chromeless et transparente pour un effet de popup flottant.
/// </summary>
public partial class LoginWindow
{
    public LoginViewModel ViewModel { get; }

    public LoginWindow(LoginViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();

        // Afficher la version dans le badge DEV
        VersionRun.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        // Créer LoginPage avec le même ViewModel et l'afficher dans le Frame
        var loginPage = new LoginPage(viewModel);
        PageHost.Content = loginPage;

        // Subscribe à l'événement de connexion réussie
        ViewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private void OnLoginSucceeded()
    {
        // La connexion a réussi, on ferme cette window
        // MainWindow sera ouverte par ApplicationHostService
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.LoginSucceeded -= OnLoginSucceeded;
        base.OnClosed(e);
    }
}
