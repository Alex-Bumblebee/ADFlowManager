using System.Globalization;
using System.Windows.Data;

namespace ADFlowManager.UI.Converters;

/// <summary>
/// Convertit un DisplayName en initiales (max 2 caractères).
/// Ex: "John Doe" → "JD", "Alice" → "A", "" → "?"
/// </summary>
public class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpper(),
            _ => $"{parts[0][..1]}{parts[^1][..1]}".ToUpper()
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
