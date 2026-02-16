using System.Security.Cryptography;
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
    private string _userSam;

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
    /// Dictionnaire de mots internationaux simples à retenir et prononcer.
    /// </summary>
    private static readonly string[] SimpleWords =
    [
        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot",
        "Galaxy", "Hunter", "Indigo", "Jungle", "Krypto", "Lotus",
        "Mango", "Naruto", "Omega", "Phoenix", "Quartz", "Rocket",
        "Shadow", "Titan", "Ultra", "Vortex", "Winter", "Xenon",
        "Zephyr", "Blaze", "Crystal", "Dragon", "Falcon", "Glacier",
        "Horizon", "Jaguar", "Knight", "Legend", "Meteor", "Nebula",
        "Oracle", "Panther", "Raptor", "Sierra", "Thunder", "Viking",
        "Wizard", "Zenith", "Storm", "Coral", "Silver", "Amber",
        "Cobalt", "Prism", "Summit", "Velvet", "Cyber", "Plasma",
        "Quantum", "Spark", "Mystic", "Solar", "Turbo", "Pixel",
        "Matrix", "Sonic", "Atlas", "Raven", "Comet", "Lancer",
        "Sphinx", "Astral", "Carbon", "Nitro", "Pulse", "Chrome"
    ];

    private static readonly char[] SpecialChars = ['@', '#', '!', '$', '%', '&', '*'];

    public ResetPasswordDialogViewModel(ILogger<ResetPasswordDialogViewModel> logger)
    {
        _logger = logger;
        _userSam = "";
    }

    /// <summary>
    /// Initialise le dialog avec les infos de l'utilisateur cible.
    /// </summary>
    public void Initialize(string userSam, string userDisplayName)
    {
        _userSam = userSam;
        UserDisplayName = userDisplayName;
    }

    partial void OnNewPasswordChanged(string value) => ValidatePasswords();
    partial void OnConfirmPasswordChanged(string value) => ValidatePasswords();

    private void ValidatePasswords()
    {
        CanReset = !string.IsNullOrWhiteSpace(NewPassword) &&
                   NewPassword.Length >= 8 &&
                   NewPassword == ConfirmPassword;
    }

    /// <summary>
    /// Génère un mot de passe classique de 12 caractères (majuscules, minuscules, chiffres, spéciaux).
    /// </summary>
    [RelayCommand]
    private void GenerateClassic()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string special = "@#!$%&*";
        const int length = 12;

        var password = new char[length];

        // Garantir au moins un de chaque type
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = special[RandomNumberGenerator.GetInt32(special.Length)];

        // Remplir le reste
        var allChars = upper + lower + digits + special;
        for (int i = 4; i < length; i++)
            password[i] = allChars[RandomNumberGenerator.GetInt32(allChars.Length)];

        // Mélanger
        for (int i = password.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        var generated = new string(password);
        NewPassword = generated;
        ConfirmPassword = generated;
        GeneratedPasswordDisplay = generated;

        _logger.LogInformation("Mot de passe classique généré pour {User}", _userSam);
    }

    /// <summary>
    /// Génère un mot de passe simple de ~12 caractères (Mot + Chiffres + Spécial).
    /// Exemple : "Naruto239@" — facile à retenir et transmettre.
    /// </summary>
    [RelayCommand]
    private void GenerateSimple()
    {
        var word = SimpleWords[RandomNumberGenerator.GetInt32(SimpleWords.Length)];
        var number = RandomNumberGenerator.GetInt32(100, 999).ToString();
        var special = SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)];

        var generated = $"{word}{number}{special}";

        NewPassword = generated;
        ConfirmPassword = generated;
        GeneratedPasswordDisplay = generated;

        _logger.LogInformation("Mot de passe simple généré pour {User}", _userSam);
    }

}
