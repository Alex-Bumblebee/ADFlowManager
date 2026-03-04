using ADFlowManager.Core.Models;
using ADFlowManager.UI.Extensions;
using System.Resources;

namespace ADFlowManager.UI.Views.Windows;

/// <summary>
/// Fenêtre modale de détails d'un ordinateur AD.
/// Affiche les informations générales, système et groupes.
/// Design aligné sur UserDetailsWindow.
/// </summary>
public partial class ComputerDetailsWindow
{
    private readonly Computer _computer;
    private static readonly ResourceManager _rm = new("ADFlowManager.UI.Resources.Strings", typeof(ComputerDetailsWindow).Assembly);

    private static string L(string key) => _rm.GetString(key, LocalizedExtension.OverrideCulture) ?? key;

    public ComputerDetailsWindow(Computer computer)
    {
        _computer = computer;

        InitializeComponent();

        LoadComputerDetails();
    }

    private void LoadComputerDetails()
    {
        // Header
        ComputerNameText.Text = _computer.Name;
        ComputerOSText.Text = $"{_computer.OperatingSystem} {_computer.OperatingSystemVersion}";

        // Status badge
        if (_computer.Enabled)
        {
            StatusBadge.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x1A, 0x10, 0xB9, 0x81));
            StatusText.Text = L("ComputerDetails_Active");
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(16, 185, 129));
            StatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(16, 185, 129));
            DetailStatus.Text = L("ComputerDetails_Active");
            DetailStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(16, 185, 129));
        }
        else
        {
            StatusBadge.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x1A, 0xEF, 0x44, 0x44));
            StatusText.Text = L("ComputerDetails_Disabled");
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68));
            StatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68));
            DetailStatus.Text = L("ComputerDetails_Disabled");
            DetailStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68));
        }

        // Général tab - TextBox controls
        DetailName.Text = _computer.Name;
        DetailDN.Text = _computer.DistinguishedName;
        DetailDescription.Text = string.IsNullOrWhiteSpace(_computer.Description) ? "—" : _computer.Description;
        DetailLocation.Text = string.IsNullOrWhiteSpace(_computer.Location) ? "—" : _computer.Location;
        DetailManagedBy.Text = string.IsNullOrWhiteSpace(_computer.ManagedBy) ? "—" : _computer.ManagedBy;
        DetailCreated.Text = _computer.Created != DateTime.MinValue ? _computer.Created.ToString("dd/MM/yyyy HH:mm") : "—";
        DetailModified.Text = _computer.Modified != DateTime.MinValue ? _computer.Modified.ToString("dd/MM/yyyy HH:mm") : "—";

        // Système tab
        DetailOS.Text = string.IsNullOrWhiteSpace(_computer.OperatingSystem) ? "—" : _computer.OperatingSystem;
        DetailOSVersion.Text = string.IsNullOrWhiteSpace(_computer.OperatingSystemVersion) ? "—" : _computer.OperatingSystemVersion;
        DetailLastLogon.Text = _computer.LastLogon?.ToString("dd/MM/yyyy HH:mm") ?? "—";

        // Groupes tab
        GroupCountText.Text = _computer.MemberOf.Count.ToString();
        if (_computer.MemberOf.Count > 0)
        {
            GroupsList.ItemsSource = _computer.MemberOf;
        }
        else
        {
            GroupsList.ItemsSource = new[] { L("ComputerDetails_NoGroups") };
        }
    }

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }
}
