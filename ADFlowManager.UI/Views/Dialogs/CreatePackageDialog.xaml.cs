using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ADFlowManager.Core.Models.Deployment;
using ADFlowManager.UI.Extensions;
using Microsoft.Win32;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Dialog de création d'un package de déploiement.
/// Permet de remplir les infos générales, l'installateur, les prérequis,
/// et optionnellement les steps/criteria via JSON brut ou import de fichier.
/// </summary>
public partial class CreatePackageDialog
{
    private static readonly System.Resources.ResourceManager _rm =
        new("ADFlowManager.UI.Resources.Strings", typeof(CreatePackageDialog).Assembly);

    private static string L(string key) =>
        _rm.GetString(key, LocalizedExtension.OverrideCulture) ?? key;

    /// <summary>
    /// Le package créé ou modifié, disponible après confirmation.
    /// </summary>
    public DeploymentPackage? CreatedPackage { get; private set; }

    /// <summary>
    /// Indique si l'utilisateur a confirmé la création/modification.
    /// </summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// En mode édition : le package original dont on préserve l'Id et la date de création.
    /// Null en mode création.
    /// </summary>
    private DeploymentPackage? _editingPackage;

    // ===== MODE CRÉATION =====
    public CreatePackageDialog()
    {
        InitializeComponent();
        SetDefaultJsonPlaceholders();
    }

    // ===== MODE ÉDITION =====
    /// <param name="existing">Package à modifier. La clé de signature doit être disponible.</param>
    /// <param name="signerThumbprint">Empreinte de la clé (affichée dans le badge propriétaire).</param>
    public CreatePackageDialog(DeploymentPackage existing, string? signerThumbprint = null)
    {
        InitializeComponent();
        _editingPackage = existing;

        // Adapter le titre + bouton confirm
        TitleBar.Title = L("CreatePackage_EditTitle");
        DialogSubtitle.Text = L("CreatePackage_EditSubtitle");
        ConfirmButton.Content = L("CreatePackage_Save");

        // Badge propriétaire
        OwnerBadge.Visibility = Visibility.Visible;
        var keyHint = string.IsNullOrWhiteSpace(signerThumbprint)
            ? L("CreatePackage_OwnerBadge")
            : string.Format(L("CreatePackage_OwnerBadgeKey"), signerThumbprint[..8].ToUpper());
        OwnerBadgeText.Text = keyHint;

        // Pré-remplir tous les champs
        LoadForEdit(existing);
    }

    private void SetDefaultJsonPlaceholders()
    {
        StepsJson.Text = """
            [
              {
                "Order": 1,
                "Type": "install",
                "Action": "execute",
                "Parameters": {}
              }
            ]
            """;

        CriteriaJson.Text = """
            {
              "Type": "exitCode",
              "Path": "",
              "ExpectedValue": 0
            }
            """;
    }

    private void LoadForEdit(DeploymentPackage pkg)
    {
        PackageName.Text = pkg.Name;
        PackageVersion.Text = pkg.Version;
        PackageCategory.Text = pkg.Category;
        PackageDescription.Text = pkg.Description;
        PackageAuthor.Text = pkg.Author;
        PackageTags.Text = string.Join(", ", pkg.Tags);

        // Type installateur
        SetInstallerType(pkg.Installer.Type);
        InstallerPath.Text = pkg.Installer.Path;
        InstallerArguments.Text = pkg.Installer.Arguments;
        InstallerHashBox.Text = pkg.Installer.InstallerHash;
        InstallerTimeout.Text = pkg.Installer.TimeoutSeconds.ToString();

        // Architecture
        for (int i = 0; i < ReqArchitecture.Items.Count; i++)
        {
            if (((System.Windows.Controls.ComboBoxItem)ReqArchitecture.Items[i]).Content?.ToString() == pkg.Requirements.Architecture)
            { ReqArchitecture.SelectedIndex = i; break; }
        }
        ReqMinWindows.Text = pkg.Requirements.MinWindowsVersion;
        ReqMinDisk.Text    = pkg.Requirements.MinDiskSpaceMB.ToString();
        ReqMinRam.Text     = pkg.Requirements.MinRamMB.ToString();

        // Steps
        if (pkg.Steps.Count > 0)
            StepsJson.Text = System.Text.Json.JsonSerializer.Serialize(pkg.Steps,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        else
            SetDefaultJsonPlaceholders();

        // Criteria
        if (pkg.SuccessCriteria != null)
            CriteriaJson.Text = System.Text.Json.JsonSerializer.Serialize(pkg.SuccessCriteria,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        // Tout va bien
        ValidationDot.Fill = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(16, 185, 129));
        ValidationText.Text = L("CreatePackage_EditLoaded");
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        // Validation
        var name = PackageName.Text?.Trim();
        var version = PackageVersion.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
        {
            ValidationDot.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68));
            ValidationText.Text = L("CreatePackage_ValidationRequired");
            return;
        }

        try
        {
            var package = new DeploymentPackage
            {
                Name = name,
                Version = version,
                Category = PackageCategory.Text?.Trim() ?? "",
                Description = PackageDescription.Text?.Trim() ?? "",
                Author = PackageAuthor.Text?.Trim() ?? "",
                Tags = ParseTags(PackageTags.Text),
                Installer = new InstallerInfo
                {
                    Type = ((System.Windows.Controls.ComboBoxItem)InstallerType.SelectedItem)?.Content?.ToString() ?? "exe",
                    Path = InstallerPath.Text?.Trim() ?? "",
                    Arguments = InstallerArguments.Text?.Trim() ?? "",
                    InstallerHash = InstallerHashBox.Text?.Trim() ?? "",
                    TimeoutSeconds = int.TryParse(InstallerTimeout.Text, out var t) ? t : 0
                },
                Requirements = new PackageRequirements
                {
                    Architecture = ((System.Windows.Controls.ComboBoxItem)ReqArchitecture.SelectedItem)?.Content?.ToString() ?? "x64",
                    MinWindowsVersion = ReqMinWindows.Text?.Trim() ?? "",
                    MinDiskSpaceMB = int.TryParse(ReqMinDisk.Text, out var d) ? d : 0,
                    MinRamMB = int.TryParse(ReqMinRam.Text, out var r) ? r : 0
                }
            };

            // Parse Steps JSON
            var stepsJson = StepsJson.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(stepsJson) && stepsJson != "[]")
            {
                try
                {
                    package.Steps = JsonSerializer.Deserialize<List<DeploymentStep>>(stepsJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                }
                catch (JsonException)
                {
                    ValidationDot.Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(239, 68, 68));
                    ValidationText.Text = L("CreatePackage_InvalidStepsJson");
                    return;
                }
            }

            // Parse Success Criteria JSON
            var criteriaJson = CriteriaJson.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(criteriaJson) && criteriaJson != "{}")
            {
                try
                {
                    package.SuccessCriteria = JsonSerializer.Deserialize<SuccessCriteria>(criteriaJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException)
                {
                    ValidationDot.Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(239, 68, 68));
                    ValidationText.Text = L("CreatePackage_InvalidCriteriaJson");
                    return;
                }
            }

            CreatedPackage = package;

            // En mode édition : conserver l'Id, la date de création, et effacer la signature
            // (elle sera recalculée par le ViewModel après UpdatePackageAsync)
            if (_editingPackage != null)
            {
                package.Id = _editingPackage.Id;
                package.Created = _editingPackage.Created;
                package.Updated = DateTime.UtcNow;
                package.Signature = null; // serésigné par le ViewModel
            }

            Confirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            ValidationDot.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68));
            ValidationText.Text = $"{L("Common_Error")} : {ex.Message}";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BrowseInstaller_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("CreatePackage_BrowseInstallerTitle"),
            Filter = "Executables (*.exe;*.msi)|*.exe;*.msi|PowerShell (*.ps1)|*.ps1|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        InstallerPath.Text = dialog.FileName;

        // Compute SHA-256 hash of the installer file
        ComputeInstallerHash(dialog.FileName);

        // Auto-detect metadata from the selected file
        try
        {
            var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();

            if (ext == ".exe")
            {
                AutoDetectFromExe(dialog.FileName);
            }
            else if (ext == ".msi")
            {
                AutoDetectFromMsi(dialog.FileName);
            }
            else if (ext == ".ps1")
            {
                // Set type to ps1
                for (int i = 0; i < InstallerType.Items.Count; i++)
                {
                    if (((System.Windows.Controls.ComboBoxItem)InstallerType.Items[i]).Content?.ToString() == "ps1")
                    {
                        InstallerType.SelectedIndex = i;
                        break;
                    }
                }
                var fileName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                if (string.IsNullOrWhiteSpace(PackageName.Text))
                    PackageName.Text = fileName;

                ShowAutoDetectBanner(L("CreatePackage_AutoDetectPS1"));
            }
        }
        catch (Exception ex)
        {
            _logger("Auto-detect failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Extrait les métadonnées d'un fichier .exe via FileVersionInfo,
    /// puis détecte le framework installeur pour pré-remplir les arguments silencieux.
    /// </summary>
    private void AutoDetectFromExe(string filePath)
    {
        var info = FileVersionInfo.GetVersionInfo(filePath);

        SetInstallerType("exe");

        if (string.IsNullOrWhiteSpace(PackageName.Text) && !string.IsNullOrWhiteSpace(info.ProductName))
            PackageName.Text = info.ProductName.Trim();
        if (string.IsNullOrWhiteSpace(PackageVersion.Text) && !string.IsNullOrWhiteSpace(info.ProductVersion))
            PackageVersion.Text = info.ProductVersion.Trim();
        if (string.IsNullOrWhiteSpace(PackageDescription.Text) && !string.IsNullOrWhiteSpace(info.FileDescription))
            PackageDescription.Text = info.FileDescription.Trim();
        if (string.IsNullOrWhiteSpace(PackageAuthor.Text) && !string.IsNullOrWhiteSpace(info.CompanyName))
            PackageAuthor.Text = info.CompanyName.Trim();

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(info.ProductName)) details.Add(info.ProductName);
        if (!string.IsNullOrWhiteSpace(info.ProductVersion)) details.Add($"v{info.ProductVersion}");
        if (!string.IsNullOrWhiteSpace(info.CompanyName)) details.Add(info.CompanyName);

        // Détection du framework installeur par scan binaire
        var framework = DetectInstallerFramework(filePath);
        if (framework.HasValue)
        {
            if (string.IsNullOrWhiteSpace(InstallerArguments.Text))
                InstallerArguments.Text = framework.Value.Args;
            details.Add(string.Format(L("CreatePackage_FrameworkDetected"), framework.Value.Name, framework.Value.Args));
        }

        ShowAutoDetectBanner(string.Format(L("CreatePackage_AutoDetectSuccess"), string.Join(" • ", details)));
    }

    /// <summary>
    /// Détecte le framework installeur d'un EXE en scannant les premiers 512 Ko du binaire.
    /// Retourne le nom du framework et les arguments silencieux suggérés, ou null si non reconnu.
    /// </summary>
    private static (string Name, string Args)? DetectInstallerFramework(string filePath)
    {
        try
        {
            const int scanSize = 524288; // 512 Ko
            using var fs = new System.IO.FileStream(
                filePath, System.IO.FileMode.Open,
                System.IO.FileAccess.Read, System.IO.FileShare.Read);
            var buffer = new byte[Math.Min(scanSize, fs.Length)];
            _ = fs.Read(buffer, 0, buffer.Length);

            // Latin-1 pour lire les chaînes ASCII embarquées sans perte
            var text = System.Text.Encoding.Latin1.GetString(buffer);

            if (text.Contains("Inno Setup"))                            return ("Inno Setup",     "/VERYSILENT /NORESTART /SP-");
            if (text.Contains("Nullsoft.NSIS") ||
                text.Contains("Nullsoft Install") ||
                text.Contains("NSIS Error"))                            return ("NSIS",           "/S");
            if (text.Contains("WiX Burn") || text.Contains("WixBurn")) return ("WiX Burn",       "/quiet /norestart");
            if (text.Contains("InstallShield"))                         return ("InstallShield",  "-s");
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Extrait les métadonnées d'un fichier .msi via COM WindowsInstaller.Installer.
    /// </summary>
    private void AutoDetectFromMsi(string filePath)
    {
        SetInstallerType("msi");

        string? productName = null;
        string? productVersion = null;
        string? manufacturer = null;
        string? description = null;

        try
        {
            // Utiliser COM WindowsInstaller pour lire les propriétés MSI
            var installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
            if (installerType == null)
            {
                // Fallback: just use file name
                FallbackFromFileName(filePath);
                return;
            }

            dynamic installer = Activator.CreateInstance(installerType)!;
            dynamic database = installer.OpenDatabase(filePath, 0); // 0 = msiOpenDatabaseModeReadOnly

            productName = GetMsiProperty(database, "ProductName");
            productVersion = GetMsiProperty(database, "ProductVersion");
            manufacturer = GetMsiProperty(database, "Manufacturer");
            description = GetMsiProperty(database, "ARPCOMMENTS");
            if (string.IsNullOrWhiteSpace(description))
                description = GetMsiProperty(database, "ProductName");

            Marshal.ReleaseComObject(database);
            Marshal.ReleaseComObject(installer);
        }
        catch
        {
            // Fallback si COM échoue
            FallbackFromFileName(filePath);
            return;
        }

        if (string.IsNullOrWhiteSpace(PackageName.Text) && !string.IsNullOrWhiteSpace(productName))
            PackageName.Text = productName.Trim();

        if (string.IsNullOrWhiteSpace(PackageVersion.Text) && !string.IsNullOrWhiteSpace(productVersion))
            PackageVersion.Text = productVersion.Trim();

        if (string.IsNullOrWhiteSpace(PackageDescription.Text) && !string.IsNullOrWhiteSpace(description))
            PackageDescription.Text = description.Trim();

        if (string.IsNullOrWhiteSpace(PackageAuthor.Text) && !string.IsNullOrWhiteSpace(manufacturer))
            PackageAuthor.Text = manufacturer.Trim();

        // Default MSI arguments if empty
        if (string.IsNullOrWhiteSpace(InstallerArguments.Text))
            InstallerArguments.Text = "/qn /norestart";

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(productName)) details.Add(productName);
        if (!string.IsNullOrWhiteSpace(productVersion)) details.Add($"v{productVersion}");
        if (!string.IsNullOrWhiteSpace(manufacturer)) details.Add(manufacturer);

        ShowAutoDetectBanner(string.Format(L("CreatePackage_AutoDetectSuccess"), string.Join(" • ", details)));
    }

    /// <summary>
    /// Lit une propriété depuis la table Property d'un fichier MSI.
    /// </summary>
    private static string? GetMsiProperty(dynamic database, string propertyName)
    {
        try
        {
            dynamic view = database.OpenView($"SELECT Value FROM Property WHERE Property = '{propertyName}'");
            view.Execute();
            dynamic record = view.Fetch();
            if (record != null)
            {
                string value = record.StringData[1];
                Marshal.ReleaseComObject(record);
                Marshal.ReleaseComObject(view);
                return value;
            }
            Marshal.ReleaseComObject(view);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Fallback : déduit le nom depuis le nom de fichier.
    /// </summary>
    private void FallbackFromFileName(string filePath)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(PackageName.Text))
            PackageName.Text = fileName;

        ShowAutoDetectBanner(L("CreatePackage_AutoDetectPartial"));
    }

    private void SetInstallerType(string type)
    {
        for (int i = 0; i < InstallerType.Items.Count; i++)
        {
            if (((System.Windows.Controls.ComboBoxItem)InstallerType.Items[i]).Content?.ToString() == type)
            {
                InstallerType.SelectedIndex = i;
                break;
            }
        }
    }

    private void ShowAutoDetectBanner(string message)
    {
        AutoDetectBanner.Visibility = Visibility.Visible;
        AutoDetectText.Text = message;

        ValidationDot.Fill = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(16, 185, 129));
        ValidationText.Text = L("CreatePackage_AutoDetectDone");
    }

    /// <summary>
    /// Calcule le hash SHA-256 du fichier installateur sélectionné.
    /// </summary>
    private void ComputeInstallerHash(string filePath)
    {
        try
        {
            InstallerHashBox.Text = L("CreatePackage_HashComputing");

            using var stream = System.IO.File.OpenRead(filePath);
            var hashBytes = SHA256.HashData(stream);
            InstallerHashBox.Text = Convert.ToHexString(hashBytes).ToLowerInvariant();
            InstallerHashBox.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(163, 163, 174)); // #A3A3AE
        }
        catch (Exception ex)
        {
            InstallerHashBox.Text = $"Erreur : {ex.Message}";
            InstallerHashBox.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68)); // red
        }
    }

    private static void _logger(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[CreatePackageDialog] {message}");
    }

    private void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("CreatePackage_ImportJsonTitle"),
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = System.IO.File.ReadAllText(dialog.FileName);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var package = JsonSerializer.Deserialize<DeploymentPackage>(json, options);

            if (package == null) return;

            // Remplir les champs depuis le JSON importé
            PackageName.Text = package.Name;
            PackageVersion.Text = package.Version;
            PackageCategory.Text = package.Category;
            PackageDescription.Text = package.Description;
            PackageAuthor.Text = package.Author;
            PackageTags.Text = string.Join(", ", package.Tags);

            // Installer
            for (int i = 0; i < InstallerType.Items.Count; i++)
            {
                if (((System.Windows.Controls.ComboBoxItem)InstallerType.Items[i]).Content?.ToString() == package.Installer.Type)
                {
                    InstallerType.SelectedIndex = i;
                    break;
                }
            }
            InstallerPath.Text = package.Installer.Path;
            InstallerArguments.Text = package.Installer.Arguments;
            InstallerHashBox.Text = package.Installer.InstallerHash;
            InstallerTimeout.Text = package.Installer.TimeoutSeconds.ToString();

            // Requirements
            for (int i = 0; i < ReqArchitecture.Items.Count; i++)
            {
                if (((System.Windows.Controls.ComboBoxItem)ReqArchitecture.Items[i]).Content?.ToString() == package.Requirements.Architecture)
                {
                    ReqArchitecture.SelectedIndex = i;
                    break;
                }
            }
            ReqMinWindows.Text = package.Requirements.MinWindowsVersion;
            ReqMinDisk.Text = package.Requirements.MinDiskSpaceMB.ToString();
            ReqMinRam.Text = package.Requirements.MinRamMB.ToString();

            // Steps & Criteria as JSON
            if (package.Steps.Count > 0)
            {
                StepsJson.Text = JsonSerializer.Serialize(package.Steps,
                    new JsonSerializerOptions { WriteIndented = true });
            }

            if (package.SuccessCriteria != null)
            {
                CriteriaJson.Text = JsonSerializer.Serialize(package.SuccessCriteria,
                    new JsonSerializerOptions { WriteIndented = true });
            }

            ValidationDot.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(16, 185, 129));
            ValidationText.Text = L("CreatePackage_ImportSuccess");
        }
        catch (Exception ex)
        {
            ValidationDot.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68));
            ValidationText.Text = $"{L("CreatePackage_ImportError")} : {ex.Message}";
        }
    }

    private static List<string> ParseTags(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
    }
}
