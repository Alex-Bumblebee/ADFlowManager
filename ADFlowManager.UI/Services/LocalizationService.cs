using System.Globalization;
using System.Resources;
using ADFlowManager.Core.Interfaces.Services;

namespace ADFlowManager.UI.Services;

/// <summary>
/// Service de localisation basé sur les fichiers .resx.
/// Charge les traductions depuis ADFlowManager.UI.Resources.Strings.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    private static readonly Dictionary<string, string> _availableLanguages = new()
    {
        { "fr-FR", "Français" },
        { "en-US", "English" }
    };

    public LocalizationService()
    {
        _resourceManager = new ResourceManager(
            "ADFlowManager.UI.Resources.Strings",
            typeof(LocalizationService).Assembly);
        _currentCulture = CultureInfo.CurrentUICulture;
    }

    public string GetString(string key)
    {
        try
        {
            return _resourceManager.GetString(key, _currentCulture) ?? key;
        }
        catch
        {
            return key;
        }
    }

    public string CurrentCulture => _currentCulture.Name;

    public void SetLanguage(string cultureCode)
    {
        _currentCulture = new CultureInfo(cultureCode);
        CultureInfo.CurrentUICulture = _currentCulture;
        CultureInfo.CurrentCulture = _currentCulture;
        Thread.CurrentThread.CurrentUICulture = _currentCulture;
        Thread.CurrentThread.CurrentCulture = _currentCulture;
    }

    public IReadOnlyDictionary<string, string> AvailableLanguages => _availableLanguages;
}
