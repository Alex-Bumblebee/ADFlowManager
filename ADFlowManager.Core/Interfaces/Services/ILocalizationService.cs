namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service de localisation pour l'internationalisation de l'application.
/// Fournit l'accès aux chaînes traduites et la gestion de la langue.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Récupère une chaîne traduite par sa clé.
    /// </summary>
    string GetString(string key);

    /// <summary>
    /// Code culture actuel (ex: "fr-FR", "en-US").
    /// </summary>
    string CurrentCulture { get; }

    /// <summary>
    /// Change la langue de l'application.
    /// Nécessite un redémarrage pour appliquer complètement.
    /// </summary>
    void SetLanguage(string cultureCode);

    /// <summary>
    /// Langues disponibles (code culture → nom affiché).
    /// </summary>
    IReadOnlyDictionary<string, string> AvailableLanguages { get; }
}
