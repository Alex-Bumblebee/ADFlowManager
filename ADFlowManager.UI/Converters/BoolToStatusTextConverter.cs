using System.Globalization;
using System.Windows.Data;

namespace ADFlowManager.UI.Converters;

/// <summary>
/// Convertit un booléen en texte de statut.
/// true → "Actif", false → "Désactivé".
/// </summary>
public class BoolToStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Actif" : "Désactivé";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
