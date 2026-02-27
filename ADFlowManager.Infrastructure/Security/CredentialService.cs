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
    private const string SessionCredentialTarget = "ADFlowManager.Session";

    public CredentialService(ILogger<CredentialService> logger)
    {
        _logger = logger;
    }

    public void SaveCredentials(string domain, string username, string password)
    {
        try
        {
            _logger.LogInformation("Saving persisted credentials for {Domain}/{Username}", domain, username);

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
                _logger.LogWarning("Failed to persist credentials");
                throw new InvalidOperationException("Impossible de sauvegarder les credentials");
            }

            _logger.LogInformation("Credentials saved in Windows Credential Manager");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while saving persisted credentials");
            throw;
        }
    }

    public (string? domain, string? username, string? password) LoadCredentials()
    {
        try
        {
            _logger.LogDebug("Loading persisted credentials from Windows Credential Manager");

            using var cred = new Credential
            {
                Target = CredentialTarget,
                Type = CredentialType.Generic
            };

            if (!cred.Load())
            {
                _logger.LogDebug("No persisted credentials found");
                return (null, null, null);
            }

            var parts = cred.Username?.Split('\\');
            if (parts == null || parts.Length != 2)
            {
                _logger.LogWarning("Invalid persisted credential username format: {Username}", cred.Username);
                return (null, null, null);
            }

            var domain = parts[0];
            var username = parts[1];
            var password = cred.Password;

            _logger.LogInformation("Persisted credentials loaded for {Domain}/{Username}", domain, username);

            return (domain, username, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading persisted credentials");
            return (null, null, null);
        }
    }

    public void DeleteCredentials()
    {
        try
        {
            _logger.LogInformation("Deleting persisted credentials");

            using var cred = new Credential
            {
                Target = CredentialTarget,
                Type = CredentialType.Generic
            };

            if (cred.Delete())
            {
                _logger.LogInformation("Persisted credentials deleted");
            }
            else
            {
                _logger.LogDebug("No persisted credentials to delete");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting persisted credentials");
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

    public void SaveSessionCredentials(string domain, string username, string password)
    {
        try
        {
            _logger.LogInformation("Saving session credentials for {Domain}/{Username}", domain, username);

            using var cred = new Credential
            {
                Target = SessionCredentialTarget,
                Username = $"{domain}\\{username}",
                Password = password,
                Type = CredentialType.Generic,
                PersistanceType = PersistanceType.Session
            };

            if (!cred.Save())
            {
                _logger.LogWarning("Failed to save session credentials");
                throw new InvalidOperationException("Impossible de sauvegarder les credentials session");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while saving session credentials");
            throw;
        }
    }

    public (string? domain, string? username, string? password) LoadSessionCredentials()
    {
        try
        {
            using var cred = new Credential
            {
                Target = SessionCredentialTarget,
                Type = CredentialType.Generic
            };

            if (!cred.Load())
            {
                return (null, null, null);
            }

            var parts = cred.Username?.Split('\\');
            if (parts == null || parts.Length != 2)
            {
                return (null, null, null);
            }

            return (parts[0], parts[1], cred.Password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading session credentials");
            return (null, null, null);
        }
    }

    public void DeleteSessionCredentials()
    {
        try
        {
            _logger.LogInformation("Deleting session credentials");

            using var cred = new Credential
            {
                Target = SessionCredentialTarget,
                Type = CredentialType.Generic
            };

            cred.Delete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting session credentials");
        }
    }
}
