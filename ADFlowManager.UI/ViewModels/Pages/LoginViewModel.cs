using System.Reflection;
using ADFlowManager.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// ViewModel pour la page de connexion Active Directory.
/// Gère la validation, l'état de chargement et l'appel au service AD.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IActiveDirectoryService _adService;
    private readonly ICredentialService _credentialService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<LoginViewModel> _logger;

    public LoginViewModel(IActiveDirectoryService adService, ICredentialService credentialService, ILocalizationService localization, ILogger<LoginViewModel> logger)
    {
        _adService = adService;
        _credentialService = credentialService;
        _localization = localization;
        _logger = logger;

        LoadSavedCredentials();
    }

    private void LoadSavedCredentials()
    {
        if (!_credentialService.HasSavedCredentials())
            return;

        var (savedDomain, savedUsername, savedPassword) = _credentialService.LoadCredentials();

        if (savedDomain != null && savedUsername != null && savedPassword != null)
        {
            Domain = savedDomain;
            Username = savedUsername;
            Password = savedPassword;
            RememberMe = true;
            _logger.LogInformation("Credentials loaded from Windows Credential Manager.");
        }
    }

    [ObservableProperty]
    private string _domain = "";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    [NotifyPropertyChangedFor(nameof(ButtonContent))]
    private bool _isLoading;

    [ObservableProperty]
    private Visibility _errorVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _appVersion = FormatVersion();

    private static string FormatVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
    }

    /// <summary>
    /// Événement déclenché quand la connexion AD réussit.
    /// Utilisé par LoginWindow pour fermer la fenêtre modale.
    /// </summary>
    public event Action? LoginSucceeded;

    /// <summary>
    /// Texte du bouton qui change selon l'état de chargement.
    /// </summary>
    public string ButtonContent => IsLoading ? _localization.GetString("Login_Connecting") : _localization.GetString("Login_Connect");

    private bool CanLogin() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        // Validation des champs
        if (string.IsNullOrWhiteSpace(Domain) ||
            string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = _localization.GetString("Login_AllFieldsRequired");
            ErrorVisibility = Visibility.Visible;
            return;
        }

        // Clear erreurs précédentes
        ErrorMessage = "";
        ErrorVisibility = Visibility.Collapsed;

        IsLoading = true;

        try
        {
            var connectTask = _adService.ConnectAsync(Domain, Username, Password);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));

            if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
            {
                ErrorMessage = _localization.GetString("Login_Timeout");
                ErrorVisibility = Visibility.Visible;
                _logger.LogWarning("AD connection timeout.");
                IsLoading = false;
                Password = string.Empty; // Nettoyage mémoire
                return;
            }

            bool success = await connectTask;

            if (success)
            {
                _logger.LogInformation("AD connection successful: {Domain}/{Username}", Domain, Username);
                ErrorMessage = "";

                // Sauvegarder ou supprimer credentials selon choix utilisateur
                if (RememberMe)
                {
                    _credentialService.SaveCredentials(Domain, Username, Password);
                    _logger.LogInformation("Credentials persisted for future sessions.");
                }
                else
                {
                    _credentialService.DeleteCredentials();
                }

                // Déclencher l'événement pour fermer LoginWindow
                LoginSucceeded?.Invoke();
            }
            else
            {
                ErrorMessage = _localization.GetString("Login_Failed");
                ErrorVisibility = Visibility.Visible;
                _logger.LogWarning("AD connection failed for {Domain}/{Username}", Domain, Username);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.GetString("Login_ErrorGeneric");
            ErrorVisibility = Visibility.Visible;
            _logger.LogError(ex, "Error during AD login.");
        }
        finally
        {
            IsLoading = false;
            Password = string.Empty; // Nettoyage mémoire du mot de passe en clair
        }
    }
}
