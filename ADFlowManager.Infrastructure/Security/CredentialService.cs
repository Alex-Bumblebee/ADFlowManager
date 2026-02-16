using ADFlowManager.Core.Interfaces.Services;
using CredentialManagement;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.Security;

/// <summary>
/// Implémentation du stockage credentials via Windows Credential Manager.
/// Les credentials sont chiffrés par Windows et stockés localement.
/// </summary>
public class CredentialService : ICredentialService
{
    private readonly ILogger<CredentialService> _logger;
    private const string CredentialTarget = "ADFlowManager";

    public CredentialService(ILogger<CredentialService> logger)
    {
        _logger = logger;
    }

    public void SaveCredentials(string domain, string username, string password)
    {
        try
        {
            _logger.LogInformation("💾 Sauvegarde credentials : {Domain}/{Username}", domain, username);

            using var cred = new Credential
            {
                Target = CredentialTarget,
                Username = $"{domain}\\{username}",
                Password = password,
                Type = CredentialType.Generic,
                PersistanceType = PersistanceType.LocalComputer
            };

            if (!cred.Save())
            {
                _logger.LogWarning("⚠️ Échec sauvegarde credentials");
                throw new InvalidOperationException("Impossible de sauvegarder les credentials");
            }

            _logger.LogInformation("✅ Credentials sauvegardés dans Windows Credential Manager");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur sauvegarde credentials");
            throw;
        }
    }

    public (string? domain, string? username, string? password) LoadCredentials()
    {
        try
        {
            _logger.LogInformation("🔍 Chargement credentials sauvegardés...");

            using var cred = new Credential
            {
                Target = CredentialTarget,
                Type = CredentialType.Generic
            };

            if (!cred.Load())
            {
                _logger.LogInformation("ℹ️ Aucun credentials sauvegardés");
                return (null, null, null);
            }

            var parts = cred.Username?.Split('\\');
            if (parts == null || parts.Length != 2)
            {
                _logger.LogWarning("⚠️ Format credentials invalide : {Username}", cred.Username);
                return (null, null, null);
            }

            var domain = parts[0];
            var username = parts[1];
            var password = cred.Password;

            _logger.LogInformation("✅ Credentials chargés : {Domain}/{Username}", domain, username);

            return (domain, username, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur chargement credentials");
            return (null, null, null);
        }
    }

    public void DeleteCredentials()
    {
        try
        {
            _logger.LogInformation("🗑️ Suppression credentials...");

            using var cred = new Credential
            {
                Target = CredentialTarget,
                Type = CredentialType.Generic
            };

            if (cred.Delete())
            {
                _logger.LogInformation("✅ Credentials supprimés");
            }
            else
            {
                _logger.LogInformation("ℹ️ Aucun credentials à supprimer");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur suppression credentials");
        }
    }

    public bool HasSavedCredentials()
    {
        try
        {
            using var cred = new Credential
            {
                Target = CredentialTarget,
                Type = CredentialType.Generic
            };

            return cred.Exists();
        }
        catch
        {
            return false;
        }
    }
}
