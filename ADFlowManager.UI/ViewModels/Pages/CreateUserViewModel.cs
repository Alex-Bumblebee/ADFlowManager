using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace ADFlowManager.UI.ViewModels.Pages;

/// <summary>
/// Unité organisationnelle (OU) pour le placement utilisateur.
/// </summary>
public class OrganizationalUnit
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
                return Name;

            var ouParts = Path
                .Split(',')
                .Where(p => p.TrimStart().StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Trim().Substring(3))
                .ToList();

            if (ouParts.Count == 0)
                return Name;

            ouParts.Reverse();
            return string.Join(" → ", ouParts);
        }
    }
}

/// <summary>
/// Wrapper groupe pour sélection checkbox dans le formulaire.
/// </summary>
public partial class GroupSelectionViewModel : ObservableObject
{
    private readonly Core.Models.Group _group;

    [ObservableProperty]
    private bool _isSelected;

    public GroupSelectionViewModel(Core.Models.Group group) => _group = group;

    public string GroupName => _group.GroupName;
    public string Description => _group.Description;
}

/// <summary>
/// ViewModel de la page de création utilisateur.
/// Templates prédéfinis, auto-génération des champs, validation temps réel.
/// </summary>
public partial class CreateUserViewModel : ObservableObject
{
    private readonly IActiveDirectoryService _adService;
    private readonly ISettingsService _settingsService;
    private readonly ITemplateService _templateService;
    private readonly ILogger<CreateUserViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly ILocalizationService _localization;

    // === Identité ===
    [ObservableProperty]
    private string _firstName = "";

    [ObservableProperty]
    private string _lastName = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _samAccountName = "";

    [ObservableProperty]
    private string _userPrincipalName = "";

    // === Contact ===
    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _phoneNumber = "";

    [ObservableProperty]
    private string _mobileNumber = "";

    // === Organisation ===
    [ObservableProperty]
    private string _jobTitle = "";

    [ObservableProperty]
    private string _department = "";

    [ObservableProperty]
    private string _company = "";

    [ObservableProperty]
    private string _office = "";

    [ObservableProperty]
    private string _description = "";

    // === Options ===
    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _passwordConfirm = "";

    [ObservableProperty]
    private bool _mustChangePasswordNextLogon = true;

    [ObservableProperty]
    private bool _passwordNeverExpires;

    [ObservableProperty]
    private bool _accountDisabled;

    // === Password Strength ===
    [ObservableProperty]
    private int _passwordStrength;

    [ObservableProperty]
    private string _passwordStrengthText = "Aucun";

    [ObservableProperty]
    private string _passwordStrengthColor = "#71717A";

    // === Mode de création (Template ou Copie user) ===
    [ObservableProperty]
    private bool _useTemplateMode = true; // true = Template, false = Copie user

    // === Templates & OU ===
    [ObservableProperty]
    private ObservableCollection<UserTemplate> _availableTemplates = [];

    [ObservableProperty]
    private UserTemplate? _selectedTemplate;

    // === Copie utilisateur ===
    private List<User> _allUsers = [];

    [ObservableProperty]
    private ObservableCollection<User> _filteredUsers = [];

    [ObservableProperty]
    private string _userSearchQuery = "";

    [ObservableProperty]
    private User? _selectedSourceUser;

    [ObservableProperty]
    private bool _isUserSearchDropdownOpen;

    [ObservableProperty]
    private ObservableCollection<OrganizationalUnit> _availableOUs = [];

    [ObservableProperty]
    private OrganizationalUnit? _selectedOU;

    // === Groupes ===
    [ObservableProperty]
    private ObservableCollection<GroupSelectionViewModel> _availableGroups = [];

    [ObservableProperty]
    private ObservableCollection<GroupSelectionViewModel> _filteredAvailableGroups = [];

    [ObservableProperty]
    private string _groupSearchQuery = "";

    [ObservableProperty]
    private int _selectedGroupsCount;

    // === UI State ===
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateUserCommand))]
    private bool _isCreating;

    // === Preview ===
    [ObservableProperty]
    private string _initials = "??";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateUserCommand))]
    private bool _isFormValid;

    [ObservableProperty]
    private string _validationMessage = "";

    public CreateUserViewModel(
        IActiveDirectoryService adService,
        ISettingsService settingsService,
        ITemplateService templateService,
        ILogger<CreateUserViewModel> logger,
        INavigationService navigationService,
        ILocalizationService localization)
    {
        _adService = adService;
        _settingsService = settingsService;
        _templateService = templateService;
        _logger = logger;
        _navigationService = navigationService;
        _localization = localization;

        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // Templates depuis le service (JSON local ou réseau)
            await LoadTemplatesAsync();

            // OUs disponibles — construire le suffixe DC depuis le domaine connecté
            var domainDN = BuildDomainDN(_adService.ConnectedDomain ?? "contoso.local");

            AvailableOUs =
            [
                new() { Name = "Users (racine)", Path = domainDN },
                new() { Name = "Users", Path = $"CN=Users,{domainDN}" },
                new() { Name = "Computers", Path = $"CN=Computers,{domainDN}" },
            ];

            // Tenter de charger les OUs réelles depuis AD
            try
            {
                var realOUs = await _adService.GetOrganizationalUnitsAsync();
                if (realOUs.Count > 0)
                {
                    AvailableOUs = new ObservableCollection<OrganizationalUnit>(
                        realOUs.Select(ou => new OrganizationalUnit { Name = ou.Name, Path = ou.Path }));
                    _logger.LogInformation("{Count} OUs chargées depuis AD", AvailableOUs.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible de charger les OUs depuis AD, utilisation des valeurs par défaut");
            }

            ApplyPreferredDefaultOu();

            // Charger groupes depuis AD
            var groups = await _adService.GetGroupsAsync();
            var groupVMs = groups.Select(g =>
            {
                var gvm = new GroupSelectionViewModel(g);
                gvm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(GroupSelectionViewModel.IsSelected))
                        UpdateSelectedGroupsCount();
                };
                return gvm;
            }).ToList();

            AvailableGroups = new ObservableCollection<GroupSelectionViewModel>(groupVMs);
            FilteredAvailableGroups = new ObservableCollection<GroupSelectionViewModel>(groupVMs);

            // Charger utilisateurs pour la fonction "Copier de"
            var users = await _adService.GetUsersAsync();
            _allUsers = users.ToList();

            _logger.LogInformation("Formulaire création utilisateur initialisé : {GroupCount} groupes, {UserCount} utilisateurs disponibles", groups.Count, users.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'initialisation du formulaire");
        }
    }

    // === Auto-génération ===
    partial void OnFirstNameChanged(string value) => GenerateFields();
    partial void OnLastNameChanged(string value) => GenerateFields();
    partial void OnPasswordChanged(string value)
    {
        CalculatePasswordStrength(value);
        ValidateForm();
    }

    partial void OnPasswordConfirmChanged(string value) => ValidateForm();

    private void GenerateFields()
    {
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
        {
            Initials = "??";
            ValidateForm();
            return;
        }

        DisplayName = $"{FirstName} {LastName}";

        var sam = $"{FirstName.ToLower()}.{LastName.ToLower()}"
            .Replace(" ", "")
            .Replace("é", "e").Replace("è", "e").Replace("ê", "e")
            .Replace("à", "a").Replace("â", "a")
            .Replace("ô", "o").Replace("ù", "u").Replace("ç", "c");
        SamAccountName = sam.Length > 20 ? sam[..20] : sam;

        UserPrincipalName = $"{SamAccountName}@{_adService.ConnectedDomain ?? "contoso.local"}";
        Email = $"{SamAccountName}@{_adService.ConnectedDomain ?? "contoso.local"}";

        Initials = $"{FirstName[0]}{LastName[0]}".ToUpper();

        ValidateForm();
    }

    // === Mode sélection (Template ou Copie user) ===
    partial void OnUseTemplateModeChanged(bool value)
    {
        if (value)
        {
            // Mode Template : réinitialiser la recherche user
            UserSearchQuery = "";
            SelectedSourceUser = null;
            IsUserSearchDropdownOpen = false;
        }
        else
        {
            // Mode Copie user : réinitialiser le template
            SelectedTemplate = AvailableTemplates.FirstOrDefault();
        }
    }

    // === Template ===
    private async Task LoadTemplatesAsync()
    {
        try
        {
            var templates = await _templateService.GetAllTemplatesAsync();

            AvailableTemplates.Clear();

            // Option "Aucun template" en premier
            AvailableTemplates.Add(new UserTemplate { Id = "", Name = "Aucun template", Description = "Partir de z\u00e9ro" });

            foreach (var template in templates)
            {
                AvailableTemplates.Add(template);
            }

            SelectedTemplate = AvailableTemplates[0];

            _logger.LogInformation("{Count} templates charg\u00e9s", templates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement templates");
        }
    }

    partial void OnSelectedTemplateChanged(UserTemplate? value)
    {
        if (!UseTemplateMode) return;
        if (value is null || string.IsNullOrWhiteSpace(value.Id)) return;
        ApplyTemplate(value);
    }

    private void ApplyTemplate(UserTemplate template)
    {
        _logger.LogInformation("Application template : {Name}", template.Name);

        // Propri\u00e9t\u00e9s organisation
        JobTitle = template.JobTitle ?? "";
        Department = template.Department ?? "";
        Company = template.Company ?? "";
        Office = template.Office ?? "";
        Description = template.UserDescription ?? "";

        // Options
        MustChangePasswordNextLogon = template.MustChangePasswordAtLogon;
        AccountDisabled = !template.IsEnabled;

        // Groupes
        foreach (var g in AvailableGroups)
            g.IsSelected = false;

        foreach (var groupName in template.Groups)
        {
            var group = AvailableGroups.FirstOrDefault(g =>
                g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            if (group is not null)
                group.IsSelected = true;
        }

        UpdateSelectedGroupsCount();

        _logger.LogInformation("Template appliqu\u00e9 : {Groups} groupes pr\u00e9-s\u00e9lectionn\u00e9s", template.Groups.Count);
    }

    // === Copie utilisateur via Dialog ===
    partial void OnUserSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredUsers.Clear();
            IsUserSearchDropdownOpen = false;
            return;
        }

        var search = value.Trim().ToLowerInvariant();
        var filtered = _allUsers.Where(u =>
            u.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            u.UserName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            u.Email.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();

        FilteredUsers = new ObservableCollection<User>(filtered);
        IsUserSearchDropdownOpen = filtered.Any();
    }

    partial void OnSelectedSourceUserChanged(User? value)
    {
        if (value is null || UseTemplateMode) return;
        ApplyCopiedUserData(value);
    }

    [RelayCommand]
    private void SelectUser(User user)
    {
        // Passer en mode copie automatiquement
        UseTemplateMode = false;

        SelectedSourceUser = user;
        ApplyCopiedUserData(user);
    }

    /// <summary>
    /// Applique les données d'un user source sélectionné depuis le dropdown inline.
    /// Copie : JobTitle, Department, Company, Office, Description + Groupes.
    /// </summary>
    private void ApplyCopiedUserData(User sourceUser)
    {
        _logger.LogInformation("Copie des droits depuis : {DisplayName} ({UserName})", sourceUser.DisplayName, sourceUser.UserName);

        // Copier les champs organisationnels
        JobTitle = sourceUser.JobTitle;
        Department = sourceUser.Department;
        Company = sourceUser.Company;
        Office = sourceUser.Office;
        Description = sourceUser.Description;

        // Copier l'OU de la personne source (extraite du DistinguishedName)
        if (!string.IsNullOrWhiteSpace(sourceUser.DistinguishedName))
        {
            // DN = "CN=Jean Dupont,OU=IT,OU=Users,DC=cours,DC=local" → on extrait tout après le premier ","
            var dnParts = sourceUser.DistinguishedName;
            var commaIndex = dnParts.IndexOf(',');
            if (commaIndex >= 0)
            {
                var ouPath = dnParts[(commaIndex + 1)..];
                var matchingOU = AvailableOUs.FirstOrDefault(ou =>
                    ou.Path.Equals(ouPath, StringComparison.OrdinalIgnoreCase));
                if (matchingOU != null)
                {
                    SelectedOU = matchingOU;
                    _logger.LogInformation("OU copiée depuis source : {OU}", matchingOU.Name);
                }
                else
                {
                    // Ajouter l'OU si elle n'est pas dans la liste
                    var newOU = new OrganizationalUnit { Name = ouPath, Path = ouPath };
                    AvailableOUs.Add(newOU);
                    SelectedOU = newOU;
                    _logger.LogInformation("OU ajoutée depuis source : {OU}", ouPath);
                }
            }
        }

        // Copier les groupes
        foreach (var g in AvailableGroups)
            g.IsSelected = false;

        foreach (var sourceGroup in sourceUser.Groups)
        {
            var group = AvailableGroups.FirstOrDefault(g =>
                g.GroupName.Equals(sourceGroup.GroupName, StringComparison.OrdinalIgnoreCase));
            if (group is not null)
                group.IsSelected = true;
        }

        UpdateSelectedGroupsCount();

        // Fermer le dropdown
        IsUserSearchDropdownOpen = false;
        UserSearchQuery = sourceUser.DisplayName;
    }

    // === Groupes search ===
    partial void OnGroupSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredAvailableGroups = new ObservableCollection<GroupSelectionViewModel>(AvailableGroups);
            return;
        }

        var search = value.Trim().ToLowerInvariant();
        var filtered = AvailableGroups.Where(g =>
            g.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            g.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        FilteredAvailableGroups = new ObservableCollection<GroupSelectionViewModel>(filtered);
    }

    private void UpdateSelectedGroupsCount()
    {
        SelectedGroupsCount = AvailableGroups.Count(g => g.IsSelected);
        ValidateForm();
    }

    // === Password Generation ===

    [RelayCommand]
    private void GeneratePassword()
    {
        Password = GenerateSecurePassword(14);
        PasswordConfirm = Password;
        _logger.LogInformation("\U0001f511 Mot de passe g\u00e9n\u00e9r\u00e9 automatiquement");
    }

    private static string GenerateSecurePassword(int length)
    {
        const string lowercase = "abcdefghijklmnopqrstuvwxyz";
        const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string digits = "0123456789";
        const string special = "!@#$%&*()-_=+[]{}|;:,.<>?";
        var all = lowercase + uppercase + digits + special;

        using var rng = RandomNumberGenerator.Create();
        var password = new char[length];

        // Garantir au moins 1 de chaque type
        password[0] = lowercase[GetRandomInt(rng, lowercase.Length)];
        password[1] = uppercase[GetRandomInt(rng, uppercase.Length)];
        password[2] = digits[GetRandomInt(rng, digits.Length)];
        password[3] = special[GetRandomInt(rng, special.Length)];

        for (int i = 4; i < length; i++)
            password[i] = all[GetRandomInt(rng, all.Length)];

        // M\u00e9langer
        return new string(password.OrderBy(_ => GetRandomInt(rng, 1000)).ToArray());
    }

    private static int GetRandomInt(RandomNumberGenerator rng, int max)
    {
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        return Math.Abs(BitConverter.ToInt32(bytes, 0)) % max;
    }

    private void CalculatePasswordStrength(string pwd)
    {
        if (string.IsNullOrWhiteSpace(pwd))
        {
            PasswordStrength = 0;
            PasswordStrengthText = "Aucun";
            PasswordStrengthColor = "#71717A";
            return;
        }

        int score = 0;
        if (pwd.Length >= 8) score += 20;
        if (pwd.Length >= 12) score += 20;
        if (pwd.Length >= 16) score += 10;
        if (Regex.IsMatch(pwd, @"[a-z]")) score += 10;
        if (Regex.IsMatch(pwd, @"[A-Z]")) score += 10;
        if (Regex.IsMatch(pwd, @"\d")) score += 10;
        if (Regex.IsMatch(pwd, @"[^a-zA-Z0-9]")) score += 20;

        PasswordStrength = Math.Min(score, 100);

        if (score < 40)
        {
            PasswordStrengthText = "Faible";
            PasswordStrengthColor = "#EF4444";
        }
        else if (score < 70)
        {
            PasswordStrengthText = "Moyen";
            PasswordStrengthColor = "#F59E0B";
        }
        else
        {
            PasswordStrengthText = "Fort";
            PasswordStrengthColor = "#10B981";
        }
    }

    // === Validation ===
    private void ValidateForm()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(FirstName))
            errors.Add("Pr\u00e9nom requis");
        if (string.IsNullOrWhiteSpace(LastName))
            errors.Add("Nom requis");
        if (string.IsNullOrWhiteSpace(SamAccountName))
            errors.Add("Login SAM requis");
        else if (SamAccountName.Length > 20)
            errors.Add("Login SAM max 20 caract\u00e8res");
        if (string.IsNullOrWhiteSpace(Password))
            errors.Add("Mot de passe requis");
        else if (Password.Length < 8)
            errors.Add("Mot de passe min 8 caract\u00e8res");
        if (Password != PasswordConfirm)
            errors.Add("Mots de passe diff\u00e9rents");
        if (SelectedOU == null)
            errors.Add("OU destination requise");

        IsFormValid = errors.Count == 0;
        ValidationMessage = errors.Count > 0 ? string.Join(" \u2022 ", errors) : "";
    }

    // === Commands ===
    private bool CanCreateUser() => IsFormValid && !IsCreating;

    [RelayCommand(CanExecute = nameof(CanCreateUser))]
    private async Task CreateUserAsync()
    {
        IsCreating = true;

        try
        {
            _logger.LogInformation("\u2795 Cr\u00e9ation utilisateur : {SAM} ({DisplayName})", SamAccountName, DisplayName);
            _logger.LogInformation("  OU: {OU}", SelectedOU?.Path);
            _logger.LogInformation("  Groupes: {Count}", SelectedGroupsCount);

            // Construire le mod\u00e8le User
            var user = new User
            {
                UserName = SamAccountName,
                FirstName = FirstName,
                LastName = LastName,
                DisplayName = DisplayName,
                UserPrincipalName = UserPrincipalName,
                Email = Email,
                Phone = PhoneNumber,
                Mobile = MobileNumber,
                JobTitle = JobTitle,
                Department = Department,
                Company = Company,
                Office = Office,
                Description = Description,
                IsEnabled = !AccountDisabled
            };

            // Cr\u00e9er l'utilisateur dans AD
            var createdUser = await _adService.CreateUserAsync(
                user,
                SelectedOU?.Path ?? BuildDomainDN(_adService.ConnectedDomain ?? "contoso.local"),
                Password,
                MustChangePasswordNextLogon,
                PasswordNeverExpires,
                AccountDisabled);

            _logger.LogInformation("\u2705 Utilisateur cr\u00e9\u00e9 : {SAM}", createdUser.UserName);

            // Ajouter aux groupes s\u00e9lectionn\u00e9s
            var selectedGroups = AvailableGroups.Where(g => g.IsSelected).ToList();
            if (selectedGroups.Count > 0)
            {
                _logger.LogInformation("\ud83d\udc65 Ajout \u00e0 {Count} groupes...", selectedGroups.Count);
                foreach (var group in selectedGroups)
                {
                    try
                    {
                        await _adService.AddUserToGroupAsync(createdUser.UserName, group.GroupName);
                        _logger.LogInformation("\u2705 Ajout\u00e9 au groupe : {Group}", group.GroupName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "\u26a0\ufe0f \u00c9chec ajout groupe : {Group}", group.GroupName);
                    }
                }
            }

            // Succès
            MessageBox.Show(
                string.Format(_localization.GetString("CreateUser_SuccessDetail"),
                    createdUser.UserName, createdUser.DisplayName, SelectedOU?.Name, selectedGroups.Count),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _logger.LogInformation("\ud83c\udf89 Cr\u00e9ation utilisateur termin\u00e9e");

            // Retour \u00e0 la page utilisateurs
            _navigationService.Navigate(typeof(Views.Pages.UsersPage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "\u274c Erreur cr\u00e9ation utilisateur");

            // Message explicite selon le type d'erreur
            var msg = ex.Message;
            if (ex.Message.Contains("r\u00e9f\u00e9rence", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("referral", StringComparison.OrdinalIgnoreCase))
            {
                msg = string.Format(_localization.GetString("CreateUser_ErrorOUNotFound"), SelectedOU?.Path);
            }
            else if (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase) ||
                     ex.Message.Contains("existe d\u00e9j\u00e0", StringComparison.OrdinalIgnoreCase))
            {
                msg = string.Format(_localization.GetString("CreateUser_ErrorAlreadyExists"), SamAccountName);
            }
            else if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                     ex.Message.Contains("mot de passe", StringComparison.OrdinalIgnoreCase))
            {
                msg = _localization.GetString("CreateUser_ErrorPasswordPolicy");
            }

            MessageBox.Show(
                string.Format(_localization.GetString("CreateUser_ErrorCreation"), msg),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsCreating = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigationService.Navigate(typeof(Views.Pages.UsersPage));
    }

    [RelayCommand]
    private async Task SaveAsTemplateAsync()
    {
        try
        {
            _logger.LogInformation("Sauvegarde comme template demand\u00e9e");

            // Dialog nom template
            var dialog = new Views.Dialogs.InputDialog(
                _localization.GetString("CreateUser_SaveTemplateTitle"),
                _localization.GetString("CreateUser_SaveTemplateName"),
                _localization.GetString("CreateUser_SaveTemplateNameHint"));

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResultText))
                return;

            var templateName = dialog.ResultText;

            // Dialog description (optionnel)
            var descDialog = new Views.Dialogs.InputDialog(
                _localization.GetString("CreateUser_SaveTemplateDescTitle"),
                _localization.GetString("CreateUser_SaveTemplateDesc"),
                _localization.GetString("CreateUser_SaveTemplateDescHint"));

            var description = descDialog.ShowDialog() == true ? descDialog.ResultText : "";

            // Cr\u00e9er template depuis les valeurs actuelles du formulaire
            var template = new UserTemplate
            {
                Name = templateName,
                Description = description ?? "",
                JobTitle = JobTitle,
                Department = Department,
                Company = Company,
                Office = Office,
                UserDescription = Description,
                Groups = AvailableGroups.Where(g => g.IsSelected).Select(g => g.GroupName).ToList(),
                MustChangePasswordAtLogon = MustChangePasswordNextLogon,
                IsEnabled = !AccountDisabled
            };

            await _templateService.SaveTemplateAsync(template);

            // Recharger la liste templates
            await LoadTemplatesAsync();

            MessageBox.Show(
                string.Format(_localization.GetString("CreateUser_TemplateSaved"), templateName),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _logger.LogInformation("Template sauvegard\u00e9 : {Name}", templateName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur sauvegarde template");
            MessageBox.Show(
                string.Format(_localization.GetString("Common_ErrorFormat"), ex.Message),
                _localization.GetString("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SetTemplateMode(bool useTemplate)
    {
        UseTemplateMode = useTemplate;
    }

    /// <summary>
    /// Convertit "cours.local" en "DC=cours,DC=local"
    /// </summary>
    private static string BuildDomainDN(string domain)
    {
        var parts = domain.Split('.');
        return string.Join(",", parts.Select(p => $"DC={p}"));
    }

    private void ApplyPreferredDefaultOu()
    {
        if (AvailableOUs.Count == 0)
            return;

        var defaultOuPath = _settingsService.CurrentSettings.ActiveDirectory.DefaultUserOU?.Trim();

        if (string.IsNullOrWhiteSpace(defaultOuPath))
        {
            SelectedOU ??= AvailableOUs[0];
            return;
        }

        var preferred = AvailableOUs.FirstOrDefault(ou =>
            ou.Path.Equals(defaultOuPath, StringComparison.OrdinalIgnoreCase));

        if (preferred is null)
        {
            preferred = new OrganizationalUnit { Name = defaultOuPath, Path = defaultOuPath };
        }
        else
        {
            AvailableOUs.Remove(preferred);
        }

        AvailableOUs.Insert(0, preferred);
        SelectedOU = preferred;

        _logger.LogInformation("OU par défaut appliquée depuis paramètres : {OU}", defaultOuPath);
    }
}
