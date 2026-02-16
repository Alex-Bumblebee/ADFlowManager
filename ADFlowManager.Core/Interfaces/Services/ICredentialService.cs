namespace ADFlowManager.Core.Interfaces.Services;

/// <summary>
/// Service de gestion des credentials via Windows Credential Manager.
/// Stockage sécurisé et chiffré par Windows.
/// </summary>
public interface ICredentialService
{
    void SaveCredentials(string domain, string username, string password);
    (string? domain, string? username, string? password) LoadCredentials();
    void DeleteCredentials();
    bool HasSavedCredentials();
}
