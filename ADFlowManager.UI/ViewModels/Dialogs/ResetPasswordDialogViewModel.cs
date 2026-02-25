using System.Windows;
using System.Windows.Threading;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.UI.Security;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.UI.ViewModels.Dialogs;

/// <summary>
/// ViewModel du dialog de réinitialisation de mot de passe.
/// Validation temps réel : minimum 8 caractères + confirmation identique.
/// Génération de mot de passe : classique (12 chars) ou simple (mot + chiffres + spécial).
/// </summary>
public partial class ResetPasswordDialogViewModel : ObservableObject
{
    private readonly ILogger<ResetPasswordDialogViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private string _userSam;
    private DispatcherTimer? _clipboardClearTimer;
    private int _clipboardCountdown;

    [ObservableProperty]
    private string _userDisplayName = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    [ObservableProperty]
    private bool _mustChangePasswordNextLogon = true;

    [ObservableProperty]
    private bool _canReset;

    /// <summary>
    /// Mot de passe généré visible en clair pour copier/transmettre.
    /// </summary>
    [ObservableProperty]
    private string _generatedPasswordDisplay = "";

    /// <summary>
    /// Message d'état du presse-papiers (compte à rebours auto-clear).
    /// </summary>
    [ObservableProperty]
    private string _clipboardStatus = "";

    public ResetPasswordDialogViewModel(ILogger<ResetPasswordDialogViewModel> logger, ISettingsService settingsService, ILocalizationService localization)
    {
        _logger = logger;
        _settingsService = settingsService;
        _localization = localization;
        _userSam = "";
    }

    /// <summary>
    /// Initialise le dialog avec les infos de l'utilisateur cible.
    /// </summary>
    public void Initialize(string userSam, string userDisplayName)
    {
        _userSam = userSam;
        UserDisplayName = userDisplayName;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        GeneratedPasswordDisplay = string.Empty;
        ClipboardStatus = string.Empty;
        StopClipboardTimer();
    }

    partial void OnNewPasswordChanged(string value) => ValidatePasswords();
    partial void OnConfirmPasswordChanged(string value) => ValidatePasswords();

    private void ValidatePasswords()
    {
        var policy = _settingsService.CurrentSettings.UserCreation.PasswordPolicy;
        CanReset = !string.IsNullOrWhiteSpace(NewPassword) &&
                   NewPassword == ConfirmPassword &&
                   PasswordPolicyHelper.IsCompliant(NewPassword, policy, out _);
    }

    /// <summary>
    /// Génère un mot de passe classique de 12 caractères (majuscules, minuscules, chiffres, spéciaux).
    /// </summary>
    [RelayCommand]
    private void GenerateClassic()
    {
        var policy = _settingsService.CurrentSettings.UserCreation.PasswordPolicy;
        var generated = PasswordPolicyHelper.GeneratePassword(policy);
        NewPassword = generated;
        ConfirmPassword = generated;
        GeneratedPasswordDisplay = generated;
        CopyToClipboardWithAutoClear(generated);

        _logger.LogInformation("Generated reset password for user: {User}", _userSam);
    }

    /// <summary>
    /// Génère un mot de passe simple de ~12 caractères (Mot + Chiffres + Spécial).
    /// Exemple : "Naruto239@" — facile à retenir et transmettre.
    /// </summary>
    [RelayCommand]
    private void GenerateSimple()
    {
        var policy = _settingsService.CurrentSettings.UserCreation.PasswordPolicy;
        var generated = policy == PasswordPolicyHelper.Easy
            ? PasswordPolicyHelper.GeneratePassword(PasswordPolicyHelper.Easy)
            : PasswordPolicyHelper.GeneratePassword(policy);

        NewPassword = generated;
        ConfirmPassword = generated;
        GeneratedPasswordDisplay = generated;
        CopyToClipboardWithAutoClear(generated);

        _logger.LogInformation("Generated readable reset password for user: {User}", _userSam);
    }

    /// <summary>
    /// Copie le mot de passe dans le presse-papiers et lance un timer de 60s pour l'effacer.
    /// </summary>
    public void CopyToClipboardWithAutoClear(string text)
    {
        try
        {
            Clipboard.SetText(text);
            _clipboardCountdown = 60;
            ClipboardStatus = string.Format(_localization.GetString("ResetPassword_ClipboardCountdown"), _clipboardCountdown);

            StopClipboardTimer();
            _clipboardClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clipboardClearTimer.Tick += OnClipboardTimerTick;
            _clipboardClearTimer.Start();

            _logger.LogDebug("Password copied to clipboard with 60s auto-clear.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy password to clipboard.");
        }
    }

    private void OnClipboardTimerTick(object? sender, EventArgs e)
    {
        _clipboardCountdown--;

        if (_clipboardCountdown <= 0)
        {
            try
            {
                Clipboard.Clear();
            }
            catch { /* Clipboard may be locked by another app */ }

            ClipboardStatus = _localization.GetString("ResetPassword_ClipboardCleared");
            StopClipboardTimer();
            _logger.LogDebug("Clipboard auto-cleared after timeout.");
        }
        else
        {
            ClipboardStatus = string.Format(_localization.GetString("ResetPassword_ClipboardCountdown"), _clipboardCountdown);
        }
    }

    private void StopClipboardTimer()
    {
        if (_clipboardClearTimer != null)
        {
            _clipboardClearTimer.Stop();
            _clipboardClearTimer.Tick -= OnClipboardTimerTick;
            _clipboardClearTimer = null;
        }
    }
}
