using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using ADFlowManager.UI.Security;
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
    private DispatcherTimer? _clipboardClearTimer;
    private int _clipboardCountdown;

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

    [ObservableProperty]
    private int? _expirationDays;

    [ObservableProperty]
    private DateTime? _expirationDate;

    // === Password Strength ===
    [ObservableProperty]
    private int _passwordStrength;

    [ObservableProperty]
    private string _passwordStrengthText = "Aucun";

    [ObservableProperty]
    private string _passwordStrengthColor = "#71717A";

    /// <summary>
    /// Message de sécurité presse-papiers (compte à rebours auto-clear 60s).
    /// </summary>
    [ObservableProperty]
    private string _clipboardStatus = "";

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

    [ObservableProperty]
    private bool _showSelectedOnly;

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
                    _logger.LogInformation("Loaded {Count} OUs from AD.", AvailableOUs.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to load OUs from AD, using fallback values.");
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

            _logger.LogInformation("User creation workflow completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while initializing user creation form.");
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

        var fName = FirstName.Trim();
        var lName = LastName.Trim();
        
        var settings = _settingsService.CurrentSettings.UserCreation;

        // Génération DisplayName selon format
        DisplayName = settings.DisplayNameFormat switch
        {
            "Nom Prenom" => $"{lName} {fName}",
            _ => $"{fName} {lName}" // "Prenom Nom" par défaut
        };

        // Nettoyage pour le login
        var cleanFirstName = RemoveDiacritics(fName.ToLower());
        var cleanLastName = RemoveDiacritics(lName.ToLower());

        // Génération Login (SamAccountName) selon format
        var baseSam = settings.LoginFormat switch
        {
            "P.Nom" => $"{cleanFirstName[0]}.{cleanLastName}",
            "Nom.P" => $"{cleanLastName}.{cleanFirstName[0]}",
            "Nom" => cleanLastName,
            _ => $"{cleanFirstName}.{cleanLastName}" // "Prenom.Nom" par défaut
        };

        baseSam = baseSam.Replace(" ", "");

        // Gestion des doublons
        var finalSam = baseSam;
        if (settings.DuplicateHandling == "AppendNumber" && _allUsers.Count > 0)
        {
            int counter = 1;
            while (_allUsers.Any(u => string.Equals(u.UserName, finalSam, StringComparison.OrdinalIgnoreCase)))
            {
                finalSam = $"{baseSam}{counter}";
                counter++;
            }
        }

        SamAccountName = finalSam.Length > 20 ? finalSam[..20] : finalSam;

        // Récupérer le domaine depuis les paramètres (EmailDomain), sinon le domaine AD par défaut
        var customDomain = _settingsService.CurrentSettings.UserCreation.EmailDomain;
        var targetDomain = string.IsNullOrWhiteSpace(customDomain) 
            ? (_adService.ConnectedDomain ?? "contoso.local") 
            : customDomain.Trim();

        // Enlever le @ si l'utilisateur l'a tapé par erreur dans les paramètres
        if (targetDomain.StartsWith("@"))
        {
            targetDomain = targetDomain[1..];
        }

        UserPrincipalName = $"{SamAccountName}@{targetDomain}";
        Email = $"{SamAccountName}@{targetDomain}";

        Initials = $"{fName[0]}{lName[0]}".ToUpper();

        ValidateForm();
    }

    private string RemoveDiacritics(string text)
    {
        return text
            .Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("ë", "e")
            .Replace("à", "a").Replace("â", "a").Replace("ä", "a")
            .Replace("ô", "o").Replace("ö", "o")
            .Replace("ù", "u").Replace("û", "u").Replace("ü", "u")
            .Replace("î", "i").Replace("ï", "i")
            .Replace("ç", "c");
    }

    partial void OnExpirationDaysChanged(int? value)
    {
        if (value.HasValue)
        {
            ExpirationDate = DateTime.Now.AddDays(value.Value).Date;
        }
        else
        {
            ExpirationDate = null;
        }
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
            AvailableTemplates.Add(new UserTemplate { Id = "", Name = "Aucun template", Description = "Partir de zéro" });

            foreach (var template in templates)
            {
                AvailableTemplates.Add(template);
            }

            SelectedTemplate = AvailableTemplates[0];

            _logger.LogInformation("Loaded {Count} templates.", templates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading templates.");
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
        _logger.LogInformation("Applying template: {Name}", template.Name);

        // Propriétés organisation
        JobTitle = template.JobTitle ?? "";
        Department = template.Department ?? "";
        Company = template.Company ?? "";
        Office = template.Office ?? "";

        // Expiration : calcule la date à partir d'aujourd'hui + X jours
        ExpirationDays = template.ExpirationDays;
        foreach (var groupName in template.Groups)
        {
            var group = AvailableGroups.FirstOrDefault(g =>
                g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            if (group is not null)
                group.IsSelected = true;
        }

        // OU
        if (!string.IsNullOrWhiteSpace(template.DefaultOU))
        {
            var matchingOU = AvailableOUs.FirstOrDefault(ou =>
                ou.Path.Equals(template.DefaultOU, StringComparison.OrdinalIgnoreCase));
            if (matchingOU != null)
                SelectedOU = matchingOU;
        }

        UpdateSelectedGroupsCount();

        _logger.LogInformation("Template applied: {Groups} groups pre-selected.", template.Groups.Count);
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
        _logger.LogInformation("Copying rights from source user: {UserName}", sourceUser.UserName);

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
                    _logger.LogDebug("OU copied from source user.");
                }
                else
                {
                    // Ajouter l'OU si elle n'est pas dans la liste
                    var newOU = new OrganizationalUnit { Name = ouPath, Path = ouPath };
                    AvailableOUs.Add(newOU);
                    SelectedOU = newOU;
                    _logger.LogDebug("OU added from source user DN.");
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

    // === Groupes filter ===
    partial void OnGroupSearchQueryChanged(string value) => RefreshGroupFilter();

    partial void OnShowSelectedOnlyChanged(bool value) => RefreshGroupFilter();

    [RelayCommand]
    private void ToggleShowSelectedGroups()
    {
        ShowSelectedOnly = !ShowSelectedOnly;
    }

    private void RefreshGroupFilter()
    {
        IEnumerable<GroupSelectionViewModel> filtered = AvailableGroups;

        if (ShowSelectedOnly)
        {
            filtered = filtered.Where(g => g.IsSelected);
        }
        else if (!string.IsNullOrWhiteSpace(GroupSearchQuery))
        {
            var search = GroupSearchQuery.Trim().ToLowerInvariant();
            filtered = filtered.Where(g =>
                g.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                g.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        FilteredAvailableGroups = new ObservableCollection<GroupSelectionViewModel>(filtered);
    }

    private void UpdateSelectedGroupsCount()
    {
        SelectedGroupsCount = AvailableGroups.Count(g => g.IsSelected);
        if (ShowSelectedOnly) RefreshGroupFilter();
        ValidateForm();
    }

    // === Password Generation ===

    [RelayCommand]
    private void GeneratePassword()
    {
        var policy = _settingsService.CurrentSettings.UserCreation.PasswordPolicy;
        Password = PasswordPolicyHelper.GeneratePassword(policy);
        PasswordConfirm = Password;
        CopyToClipboardWithAutoClear(Password);
        _logger.LogInformation("Generated password for user creation form.");
    }

    /// <summary>
    /// Copie le mot de passe dans le presse-papiers et lance un timer de 60s pour l'effacer.
    /// </summary>
    private void CopyToClipboardWithAutoClear(string text)
    {
        try
        {
            Clipboard.SetText(text);
            _clipboardCountdown = 60;
            ClipboardStatus = string.Format(_localization.GetString("CreateUser_ClipboardWarning"), _clipboardCountdown);

            StopClipboardTimer();
            _clipboardClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clipboardClearTimer.Tick += OnClipboardTimerTick;
            _clipboardClearTimer.Start();

            _logger.LogDebug("Generated password copied to clipboard with 60s auto-clear.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy generated password to clipboard.");
        }
    }

    private void OnClipboardTimerTick(object? sender, EventArgs e)
    {
        _clipboardCountdown--;

        if (_clipboardCountdown <= 0)
        {
            try { Clipboard.Clear(); }
            catch { /* Presse-papiers verrouillé par une autre application */ }

            ClipboardStatus = _localization.GetString("CreateUser_ClipboardCleared");
            StopClipboardTimer();
            _logger.LogDebug("Clipboard auto-cleared after timeout (user creation form).");
        }
        else
        {
            ClipboardStatus = string.Format(_localization.GetString("CreateUser_ClipboardWarning"), _clipboardCountdown);
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

    /// <summary>
    /// Caractères interdits dans sAMAccountName selon la documentation AD.
    /// </summary>
    private static readonly char[] InvalidSamChars = ['"', '/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>'];

    /// <summary>
    /// Caractères LDAP dangereux interdits dans les champs nom/prénom (injection LDAP).
    /// </summary>
    private static readonly char[] LdapInjectionChars = ['\0', '*', '(', ')', '\\', '/'];

    private static bool ContainsInvalidSamChars(string value) =>
        value.IndexOfAny(InvalidSamChars) >= 0;

    private static bool IsValidEmailFormat(string email) =>
        !string.IsNullOrWhiteSpace(email) &&
        email.Contains('@') &&
        email.IndexOf('@') > 0 &&
        email.IndexOf('@') < email.Length - 1 &&
        email.LastIndexOf('.') > email.IndexOf('@');

    private void ValidateForm()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(FirstName))
            errors.Add("Prénom requis");
        else if (FirstName.Trim().Length > 64)
            errors.Add("Prénom trop long (max 64)");
        else if (FirstName.IndexOfAny(LdapInjectionChars) >= 0)
            errors.Add(_localization.GetString("CreateUser_ErrorInvalidName"));

        if (string.IsNullOrWhiteSpace(LastName))
            errors.Add("Nom requis");
        else if (LastName.Trim().Length > 64)
            errors.Add("Nom trop long (max 64)");
        else if (LastName.IndexOfAny(LdapInjectionChars) >= 0)
            errors.Add(_localization.GetString("CreateUser_ErrorInvalidName"));

        if (string.IsNullOrWhiteSpace(SamAccountName))
            errors.Add("Login SAM requis");
        else if (SamAccountName.Length > 20)
            errors.Add("Login SAM max 20 caractères");
        else if (ContainsInvalidSamChars(SamAccountName))
            errors.Add("Login SAM contient des caractères invalides");

        if (!string.IsNullOrWhiteSpace(Email) && !IsValidEmailFormat(Email))
            errors.Add("Format email invalide");

        if (string.IsNullOrWhiteSpace(Password))
            errors.Add("Mot de passe requis");
        else
        {
            var policy = _settingsService.CurrentSettings.UserCreation.PasswordPolicy;
            if (!PasswordPolicyHelper.IsCompliant(Password, policy, out var reason))
                errors.Add(reason);
        }
        if (Password != PasswordConfirm)
            errors.Add("Mots de passe différents");
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
            _logger.LogInformation("Creating AD user: {SAM}", SamAccountName);
            _logger.LogDebug("Target OU selected for user creation.");
            _logger.LogDebug("Selected groups count: {Count}", SelectedGroupsCount);

            // Construire le modèle User
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

            // Créer l'utilisateur dans AD
            var createdUser = await _adService.CreateUserAsync(
                user,
                SelectedOU?.Path ?? BuildDomainDN(_adService.ConnectedDomain ?? "contoso.local"),
                Password,
                MustChangePasswordNextLogon,
                PasswordNeverExpires,
                AccountDisabled,
                ExpirationDate);

            _logger.LogInformation("AD user created: {SAM}", createdUser.UserName);

            // Ajouter aux groupes sélectionnés
            var selectedGroups = AvailableGroups.Where(g => g.IsSelected).ToList();
            if (selectedGroups.Count > 0)
            {
                _logger.LogInformation("Adding user to selected groups ({Count}).", selectedGroups.Count);
                foreach (var group in selectedGroups)
                {
                    try
                    {
                        await _adService.AddUserToGroupAsync(createdUser.UserName, group.GroupName);
                        _logger.LogDebug("User added to group: {Group}", group.GroupName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to add user to group: {Group}", group.GroupName);
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

            _logger.LogInformation("User creation completed.");

            // Retour à la page utilisateurs
            _navigationService.Navigate(typeof(Views.Pages.UsersPage));
            
            // Nettoyage mémoire des mots de passe
            Password = string.Empty;
            PasswordConfirm = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while creating user.");

            // Message explicite selon le type d'erreur
            var msg = ex.Message;
            if (ex.Message.Contains("référence", StringComparison.OrdinalIgnoreCase) ||
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
        // Nettoyage mémoire des mots de passe avant navigation
        Password = string.Empty;
        PasswordConfirm = string.Empty;
        StopClipboardTimer();
        _navigationService.Navigate(typeof(Views.Pages.UsersPage));
    }

    [RelayCommand]
    private void Reset()
    {
        var confirm = MessageBox.Show(
            _localization.GetString("CreateUser_ResetConfirm"),
            _localization.GetString("CreateUser_Reset"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        // Reset Identity
        FirstName = "";
        LastName = "";
        DisplayName = "";
        SamAccountName = "";
        UserPrincipalName = "";
        Email = "";
        Initials = "";

        // Reset Contact
        PhoneNumber = "";
        MobileNumber = "";

        // Reset Organization
        JobTitle = "";
        Department = "";
        Company = "";
        Office = "";
        Description = "";

        // Reset Options
        Password = "";
        PasswordConfirm = "";
        MustChangePasswordNextLogon = false;
        PasswordNeverExpires = false;
        AccountDisabled = false;
        ExpirationDate = null;
        ExpirationDays = null;

        // Reset Groups
        foreach (var group in AvailableGroups)
        {
            group.IsSelected = false;
        }

        // Reset OU
        ApplyPreferredDefaultOu();

        ValidateForm();
    }

    [RelayCommand]
    private async Task SaveAsTemplateAsync()
    {
        try
        {
            _logger.LogInformation("Save as template requested.");

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

            // Créer template depuis les valeurs actuelles du formulaire
            var template = new UserTemplate
            {
                Name = templateName,
                Description = description ?? "",
                JobTitle = JobTitle,
                Department = Department,
                Company = Company,
                Office = Office,
                UserDescription = Description,
                DefaultOU = SelectedOU?.Path,
                Groups = AvailableGroups.Where(g => g.IsSelected).Select(g => g.GroupName).ToList(),
                MustChangePasswordAtLogon = MustChangePasswordNextLogon,
                IsEnabled = !AccountDisabled,
                ExpirationDays = ExpirationDays
            };

            await _templateService.SaveTemplateAsync(template);

            // Recharger la liste templates
            await LoadTemplatesAsync();

            MessageBox.Show(
                string.Format(_localization.GetString("CreateUser_TemplateSaved"), templateName),
                _localization.GetString("Common_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _logger.LogInformation("Template saved: {Name}", templateName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while saving template.");
            MessageBox.Show(
                _localization.GetString("CreateUser_TemplateSaveError"),
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

        _logger.LogInformation("Default OU applied from settings.");
    }
}
