using ADFlowManager.Core.Models.Deployment;
using ADFlowManager.UI.Extensions;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// ViewModel léger pour l'affichage d'un package dans le dialog de confirmation.
/// </summary>
public class PackageConfirmItem
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string InstallerType { get; init; } = string.Empty;
    public string TimeoutLabel { get; init; } = string.Empty;
}

/// <summary>
/// Dialog de confirmation avant déploiement.
/// Affiche la liste des packages et des ordinateurs ciblés avec un résumé visuel.
/// </summary>
public partial class DeploymentConfirmDialog
{
    private static readonly System.Resources.ResourceManager _rm =
        new("ADFlowManager.UI.Resources.Strings", typeof(DeploymentConfirmDialog).Assembly);

    private static string L(string key) =>
        _rm.GetString(key, LocalizedExtension.OverrideCulture) ?? key;

    /// <summary>
    /// True si l'utilisateur a cliqué sur "Déployer".
    /// </summary>
    public bool Confirmed { get; private set; }

    /// <param name="packages">Packages à déployer.</param>
    /// <param name="computers">Noms des ordinateurs cibles.</param>
    public DeploymentConfirmDialog(IReadOnlyList<DeploymentPackage> packages, IReadOnlyList<string> computers)
    {
        InitializeComponent();
        Populate(packages, computers);
    }

    private void Populate(IReadOnlyList<DeploymentPackage> packages, IReadOnlyList<string> computers)
    {
        var pkgCount  = packages.Count;
        var compCount = computers.Count;
        var total     = pkgCount * compCount;

        // Bannière résumé
        SummaryTitle.Text = total == 1
            ? string.Format(L("DeployConfirm_SummaryOne"),  packages[0].Name, computers[0])
            : string.Format(L("DeployConfirm_SummaryMany"), pkgCount, compCount);

        SummarySubtitle.Text = pkgCount == 1
            ? string.Format(L("DeployConfirm_SubtitleOnePkg"),  packages[0].Name, packages[0].Version)
            : string.Format(L("DeployConfirm_SubtitleMultiPkg"), pkgCount);

        TotalBadgeText.Text = total.ToString();

        // En-têtes colonnes
        PackagesHeaderText.Text  = string.Format(L("DeployConfirm_PackagesHeader"),  pkgCount);
        ComputersHeaderText.Text = string.Format(L("DeployConfirm_ComputersHeader"), compCount);

        // Liste packages
        PackagesList.ItemsSource = packages.Select(p => new PackageConfirmItem
        {
            Name          = p.Name,
            Version       = p.Version,
            InstallerType = p.Installer.Type.ToUpperInvariant(),
            TimeoutLabel  = p.Installer.TimeoutSeconds > 0
                ? $"{p.Installer.TimeoutSeconds}s"
                : L("DeployConfirm_TimeoutAuto")
        }).ToList();

        // Liste ordinateurs
        ComputersList.ItemsSource = computers.ToList();

        // Estimation temps (optimiste : 2 min/déploiement séquentiel par PC,
        // mais on traite max 5 PCs en parallèle donc on divise)
        const int avgSecondsPerDeploy = 120;
        const int maxParallel = 5;
        var estimatedSeconds = (int)Math.Ceiling((double)compCount / maxParallel) * pkgCount * avgSecondsPerDeploy;
        EstimatedTimeText.Text = string.Format(L("DeployConfirm_EstimatedTime"),
            estimatedSeconds < 60
                ? $"{estimatedSeconds}s"
                : $"{estimatedSeconds / 60}–{estimatedSeconds / 60 + 2} min");
    }

    private void DeployButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
