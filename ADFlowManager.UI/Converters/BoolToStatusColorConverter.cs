using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ADFlowManager.UI.Converters;

/// <summary>
/// Convertit un booléen en couleur de statut.
/// true (actif) → Vert #10B981, false (désactivé) → Rouge #EF4444.
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush DisabledBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveBrush : DisabledBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
