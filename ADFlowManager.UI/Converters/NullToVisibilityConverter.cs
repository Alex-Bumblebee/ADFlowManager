using System.Globalization;
using System.Windows.Data;

namespace ADFlowManager.UI.Converters;

/// <summary>
/// Convertit null → Collapsed, non-null → Visible.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
