using ADFlowManager.Core.Models;

namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service de gestion des templates utilisateur (CRUD + Import/Export).
/// Stockage JSON local ou réseau partagé.
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// Récupérer tous les templates
    /// </summary>
    Task<List<UserTemplate>> GetAllTemplatesAsync();

    /// <summary>
    /// Récupérer un template par ID
    /// </summary>
    Task<UserTemplate?> GetTemplateByIdAsync(string id);

    /// <summary>
    /// Créer ou mettre à jour un template
    /// </summary>
    Task<UserTemplate> SaveTemplateAsync(UserTemplate template);

    /// <summary>
    /// Supprimer un template par ID
    /// </summary>
    Task DeleteTemplateAsync(string id);

    /// <summary>
    /// Exporter un template vers un fichier JSON
    /// </summary>
    Task ExportTemplateAsync(UserTemplate template, string filePath);

    /// <summary>
    /// Importer un template depuis un fichier JSON
    /// </summary>
    Task<UserTemplate> ImportTemplateAsync(string filePath);
}
