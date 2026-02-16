using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace ADFlowManager.UI.Extensions;

/// <summary>
/// Extension XAML pour charger des chaînes localisées depuis les ressources.
/// Usage : {ext:Localized Key=Dashboard_Refresh}
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocalizedExtension : MarkupExtension
{
    private static readonly ResourceManager _resourceManager =
        new("ADFlowManager.UI.Resources.Strings", typeof(LocalizedExtension).Assembly);

    /// <summary>
    /// Culture explicite à utiliser pour la résolution des ressources.
    /// Définie au démarrage de l'application, avant le parsing XAML.
    /// </summary>
    internal static CultureInfo? OverrideCulture { get; set; }

    public LocalizedExtension() { }

    public LocalizedExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Clé de la ressource à résoudre.
    /// </summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return "[MISSING KEY]";

        try
        {
            var culture = OverrideCulture ?? CultureInfo.CurrentUICulture;
            return _resourceManager.GetString(Key, culture) ?? $"[{Key}]";
        }
        catch
        {
            return $"[{Key}]";
        }
    }
}
