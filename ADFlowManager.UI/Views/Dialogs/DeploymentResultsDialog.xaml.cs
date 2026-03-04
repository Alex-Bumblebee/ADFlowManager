using System.ComponentModel;
using System.Runtime.CompilerServices;
using ADFlowManager.Core.Models.Deployment;
using ADFlowManager.UI.Extensions;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Item de résultat affiché dans la liste de la dialog de résultats de déploiement.
/// </summary>
public class DeployResultItem : INotifyPropertyChanged
{
    // ── Identity ───────────────────────────────────────────────────────────────
    public string PackageId      { get; init; } = string.Empty;
    public string PackageName    { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public string ComputerName   { get; init; } = string.Empty;

    // ── Display ────────────────────────────────────────────────────────────────
    /// <summary>"success" | "warning" | "locked" | "error"</summary>
    public string StatusKind    { get; init; } = "error";
    public string StatusLabel   { get; init; } = string.Empty;
    public string DurationLabel { get; init; } = string.Empty;

    // ── Detail rows ────────────────────────────────────────────────────────────
    public string ErrorDetail       { get; init; } = string.Empty;
    public bool   HasError          => !string.IsNullOrEmpty(ErrorDetail);

    public string HintText  { get; init; } = string.Empty;
    public bool   HasHint   => !string.IsNullOrEmpty(HintText);

    public string ResidualFilesText { get; init; } = string.Empty;
    public bool   HasResidualFiles  => !string.IsNullOrEmpty(ResidualFilesText);

    // ── Timeout offer ──────────────────────────────────────────────────────────
    public bool   ShowTimeoutOffer  { get; init; }
    public int    SuggestedTimeout  { get; init; }
    public string TimeoutOfferLabel { get; init; } = string.Empty;

    private bool _increaseTimeout;
    public bool IncreaseTimeout
    {
        get => _increaseTimeout;
        set { _increaseTimeout = value; OnPropertyChanged(); }
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Dialog de résultats de déploiement — affiche le détail de chaque résultat
/// avec des hints d'action corrective et les propositions d'augmentation de timeout.
/// </summary>
public partial class DeploymentResultsDialog
{
    private static readonly System.Resources.ResourceManager _rm =
        new("ADFlowManager.UI.Resources.Strings", typeof(DeploymentResultsDialog).Assembly);

    private static string L(string key) =>
        _rm.GetString(key, LocalizedExtension.OverrideCulture) ?? key;

    private List<DeployResultItem> _items = [];

    /// <summary>
    /// Liste des mises à jour de timeout à appliquer après fermeture.
    /// Contient (PackageId, NewTimeout) pour chaque item où l'utilisateur a coché la case.
    /// </summary>
    public IReadOnlyList<(string PackageId, int NewTimeout)> TimeoutUpdates { get; private set; } = [];

    /// <param name="results">Résultats de déploiement.</param>
    /// <param name="packages">Packages déployés (pour lire le timeout actuel).</param>
    public DeploymentResultsDialog(
        IReadOnlyList<DeploymentResult> results,
        IReadOnlyList<DeploymentPackage> packages)
    {
        InitializeComponent();
        Populate(results, packages);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void Populate(IReadOnlyList<DeploymentResult> results, IReadOnlyList<DeploymentPackage> packages)
    {
        _items = results.Select(r => BuildItem(r, packages)).ToList();
        ResultsList.ItemsSource = _items;

        // ── Counts ──────────────────────────────────────────────────────────
        int successes = _items.Count(i => i.StatusKind == "success");
        int warnings  = _items.Count(i => i.StatusKind == "warning");
        int errors    = _items.Count(i => i.StatusKind is "error" or "locked");

        // ── Summary banner ──────────────────────────────────────────────────
        SummaryTitle.Text = string.Format(L("DeployResults_Summary"), successes, warnings, errors);
        SummarySubtitle.Text = $"{results.Count} résultat(s) — " +
            (errors > 0           ? "des interventions sont requises"
             : warnings > 0       ? "vérifications recommandées"
             : "toutes les installations ont réussi");

        if (errors > 0)
        {
            SummaryIconBorder.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));
            SummaryIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle24;
        }
        else if (warnings > 0)
        {
            SummaryIconBorder.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B"));
            SummaryIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning24;
        }
        // else keep the green check default

        // ── Badges ──────────────────────────────────────────────────────────
        if (successes > 0)
        {
            SuccessBadge.Visibility = System.Windows.Visibility.Visible;
            SuccessBadgeText.Text = successes.ToString();
        }
        if (warnings > 0)
        {
            WarningBadge.Visibility = System.Windows.Visibility.Visible;
            WarningBadgeText.Text = warnings.ToString();
        }
        if (errors > 0)
        {
            ErrorBadge.Visibility = System.Windows.Visibility.Visible;
            ErrorBadgeText.Text = errors.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private DeployResultItem BuildItem(DeploymentResult r, IReadOnlyList<DeploymentPackage> packages)
    {
        var pkg = packages.FirstOrDefault(p => p.Id == r.PackageId);
        var usedTimeout  = r.TimeoutUsedSeconds > 0 ? r.TimeoutUsedSeconds : (pkg?.Installer.TimeoutSeconds ?? 120);
        var suggested    = usedTimeout * 2;

        // ── Duration label ───────────────────────────────────────────────────
        var dur = r.Duration.TotalSeconds < 1 ? "<1s"
                : r.Duration.TotalSeconds < 60 ? $"{(int)r.Duration.TotalSeconds}s"
                : $"{(int)r.Duration.TotalMinutes}m {r.Duration.Seconds}s";

        // ── Status kind + label ──────────────────────────────────────────────
        string kind, label;
        if (r.Success)
        {
            kind  = r.ExitCode == 3010 ? "warning" : "success";
            label = r.ExitCode == 3010 ? L("DeployResults_RebootRequired") : L("DeployResults_Success");
        }
        else
        {
            (kind, label) = r.ErrorCategory switch
            {
                DeploymentErrorCategory.FileLocked       => ("locked",  L("DeployResults_FileLocked")),
                DeploymentErrorCategory.Timeout          => ("error",   L("DeployResults_Timeout")),
                DeploymentErrorCategory.TimeoutProbablyOk=> ("warning", L("DeployResults_TimeoutProbablyOk")),
                DeploymentErrorCategory.InstallerNonZeroExit => ("error", L("DeployResults_Error")),
                DeploymentErrorCategory.ConnectionError  => ("error",   L("DeployResults_ConnectionError")),
                DeploymentErrorCategory.IntegrityError   => ("error",   L("DeployResults_IntegrityError")),
                _                                        => ("error",   L("DeployResults_Error"))
            };
        }

        // ── Hint text ───────────────────────────────────────────────────────
        string hint = r.ErrorCategory switch
        {
            DeploymentErrorCategory.FileLocked
                => string.Format(L("DeployResults_HintFileLocked"), r.ComputerName),
            DeploymentErrorCategory.TimeoutProbablyOk
                => L("DeployResults_HintTimeoutOk"),
            DeploymentErrorCategory.Timeout
                => string.Format(L("DeployResults_HintTimeout"), r.ComputerName),
            _ => string.Empty
        };

        // ── Residual files ──────────────────────────────────────────────────
        string residualText = r.ResidualFiles.Count > 0
            ? string.Format(L("DeployResults_HintResidual"), string.Join(", ", r.ResidualFiles))
            : string.Empty;

        // ── Timeout offer ───────────────────────────────────────────────────
        bool showTimeout = !r.Success && r.ErrorCategory is
            DeploymentErrorCategory.Timeout or DeploymentErrorCategory.TimeoutProbablyOk;

        return new DeployResultItem
        {
            PackageId      = r.PackageId,
            PackageName    = r.PackageName,
            PackageVersion = r.PackageVersion,
            ComputerName   = r.ComputerName,
            StatusKind     = kind,
            StatusLabel    = label,
            DurationLabel  = dur,
            ErrorDetail    = r.Success ? string.Empty : (r.ErrorMessage ?? string.Empty),
            HintText       = hint,
            ResidualFilesText = residualText,
            ShowTimeoutOffer  = showTimeout,
            SuggestedTimeout  = suggested,
            TimeoutOfferLabel = string.Format(L("DeployResults_TimeoutOffer"), suggested),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Collect timeout updates from checked items
        var updates = _items
            .Where(i => i.ShowTimeoutOffer && i.IncreaseTimeout)
            // Group by PackageId — take the largest suggested timeout per package
            .GroupBy(i => i.PackageId)
            .Select(g => (g.Key, g.Max(i => i.SuggestedTimeout)))
            .ToList();

        TimeoutUpdates = updates;

        if (updates.Count > 0)
        {
            TimeoutSavedText.Text = string.Format(L("DeployResults_TimeoutSaved"), updates.Count);
            TimeoutSavedText.Visibility = System.Windows.Visibility.Visible;
        }

        Close();
    }
}
