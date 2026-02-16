using System.Globalization;
using System.Windows.Data;

namespace ADFlowManager.UI.Converters;

/// <summary>
/// Convertit null → Visible, non-null → Collapsed.
/// </summary>
public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
