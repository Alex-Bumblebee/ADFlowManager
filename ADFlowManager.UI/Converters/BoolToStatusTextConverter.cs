using System.Globalization;
using System.Resources;
using System.Windows.Data;
using ADFlowManager.UI.Extensions;

namespace ADFlowManager.UI.Converters;

/// <summary>
/// Convertit un booléen en texte de statut localisé.
/// true → Common_Enabled ("Actif"), false → Common_Disabled ("Désactivé").
/// </summary>
public class BoolToStatusTextConverter : IValueConverter
{
    private static readonly ResourceManager _resourceManager =
        new("ADFlowManager.UI.Resources.Strings", typeof(BoolToStatusTextConverter).Assembly);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var cult = LocalizedExtension.OverrideCulture ?? CultureInfo.CurrentUICulture;
        return value is true
            ? (_resourceManager.GetString("Common_Enabled", cult) ?? "Actif")
            : (_resourceManager.GetString("Common_Disabled", cult) ?? "Désactivé");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
