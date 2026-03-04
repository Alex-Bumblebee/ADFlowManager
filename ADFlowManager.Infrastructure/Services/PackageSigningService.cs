using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models.Deployment;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.Services;

/// <summary>
/// Service de signature ECDSA P-256 des packages de déploiement.
/// La clé est stockée dans le magasin de certificats Windows CurrentUser\My
/// sous le CN "ADFlowManager Package Signing".
/// 
/// Données signées : "{InstallerHash}|{Name}|{Version}"
/// Algorithme : ECDSA P-256 (NIST) avec SHA-256
/// Format signature : Base64(DER)
/// </summary>
public class PackageSigningService : IPackageSigningService
{
    private readonly ILogger<PackageSigningService> _logger;

    /// <summary>
    /// Subject Name du certificat auto-signé dans le store.
    /// </summary>
    private const string CertificateSubject = "CN=ADFlowManager Package Signing";

    /// <summary>
    /// Friendly name affiché dans le gestionnaire de certificats.
    /// </summary>
    private const string CertificateFriendlyName = "ADFlowManager Package Signing Key";

    public PackageSigningService(ILogger<PackageSigningService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsSigningKeyAvailable()
    {
        using var cert = FindSigningCertificate();
        return cert != null;
    }

    /// <inheritdoc/>
    public void GetOrCreateSigningKey()
    {
        // Supprimer l'ancienne clé si elle existe
        DeleteSigningKey();

        // Générer une nouvelle paire ECDSA P-256
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest(
            CertificateSubject,
            ecdsa,
            HashAlgorithmName.SHA256);

        // Auto-signé, valide 10 ans
        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddYears(10);

        using var cert = request.CreateSelfSigned(notBefore, notAfter);

        // Exporter et réimporter avec clé privée persistable dans le store
        var pfxBytes = cert.Export(X509ContentType.Pfx);
        using var persistableCert = new X509Certificate2(
            pfxBytes,
            (string?)null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

        // Ajouter au store CurrentUser\My
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(persistableCert);
        store.Close();

        _logger.LogInformation(
            "Clé de signature ECDSA P-256 générée. Thumbprint: {Thumbprint}",
            persistableCert.Thumbprint);
    }

    /// <inheritdoc/>
    public bool SignPackage(DeploymentPackage package)
    {
        using var cert = FindSigningCertificate();
        if (cert == null)
        {
            _logger.LogWarning("Aucune clé de signature disponible. Le package ne sera pas signé.");
            return false;
        }

        using var ecdsa = cert.GetECDsaPrivateKey();
        if (ecdsa == null)
        {
            _logger.LogError("Le certificat de signature ne contient pas de clé privée ECDSA.");
            return false;
        }

        var dataToSign = BuildSignaturePayload(package);
        var signatureBytes = ecdsa.SignData(dataToSign, HashAlgorithmName.SHA256);
        package.Signature = Convert.ToBase64String(signatureBytes);

        _logger.LogInformation(
            "Package '{Name}' v{Version} signé avec succès. Thumbprint: {Thumbprint}",
            package.Name, package.Version, cert.Thumbprint);

        return true;
    }

    /// <inheritdoc/>
    public bool VerifyPackage(DeploymentPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Signature))
        {
            _logger.LogWarning("Le package '{Name}' v{Version} n'est pas signé.", package.Name, package.Version);
            return false;
        }

        using var cert = FindSigningCertificate();
        if (cert == null)
        {
            _logger.LogWarning(
                "Aucune clé de signature disponible pour vérifier le package '{Name}' v{Version}.",
                package.Name, package.Version);
            return false;
        }

        using var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa == null)
        {
            _logger.LogError("Le certificat de signature ne contient pas de clé publique ECDSA.");
            return false;
        }

        try
        {
            var dataToVerify = BuildSignaturePayload(package);
            var signatureBytes = Convert.FromBase64String(package.Signature);
            var isValid = ecdsa.VerifyData(dataToVerify, signatureBytes, HashAlgorithmName.SHA256);

            if (isValid)
            {
                _logger.LogInformation(
                    "Signature valide pour le package '{Name}' v{Version}.",
                    package.Name, package.Version);
            }
            else
            {
                _logger.LogError(
                    "Signature INVALIDE pour le package '{Name}' v{Version} ! Le package a peut-être été altéré.",
                    package.Name, package.Version);
            }

            return isValid;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex,
                "Format de signature invalide pour le package '{Name}' v{Version}.",
                package.Name, package.Version);
            return false;
        }
    }

    /// <inheritdoc/>
    public void ExportSigningKey(string pfxPath, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("Le mot de passe doit contenir au moins 8 caractères.");

        using var cert = FindSigningCertificate();
        if (cert == null)
            throw new InvalidOperationException("Aucune clé de signature à exporter.");

        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfxBytes);

        _logger.LogInformation(
            "Clé de signature exportée vers {Path}. Thumbprint: {Thumbprint}",
            pfxPath, cert.Thumbprint);
    }

    /// <inheritdoc/>
    public void ImportSigningKey(string pfxPath, string password)
    {
        if (!File.Exists(pfxPath))
            throw new FileNotFoundException("Fichier PFX introuvable.", pfxPath);

        // Charger et valider le PFX
        using var importedCert = new X509Certificate2(
            pfxPath,
            password,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

        // Vérifier que c'est bien une clé ECDSA
        if (importedCert.GetECDsaPrivateKey() == null)
            throw new InvalidOperationException(
                "Le fichier PFX ne contient pas de clé privée ECDSA valide.");

        // Supprimer l'ancienne clé
        DeleteSigningKey();

        // Ajouter au store
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(importedCert);
        store.Close();

        _logger.LogInformation(
            "Clé de signature importée depuis {Path}. Thumbprint: {Thumbprint}",
            pfxPath, importedCert.Thumbprint);
    }

    /// <inheritdoc/>
    public void DeleteSigningKey()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var certs = store.Certificates.Find(
            X509FindType.FindBySubjectDistinguishedName,
            CertificateSubject,
            validOnly: false);

        foreach (var cert in certs)
        {
            store.Remove(cert);
            _logger.LogInformation(
                "Clé de signature supprimée. Thumbprint: {Thumbprint}", cert.Thumbprint);
            cert.Dispose();
        }

        store.Close();
    }

    /// <inheritdoc/>
    public string? GetSigningKeyThumbprint()
    {
        using var cert = FindSigningCertificate();
        return cert?.Thumbprint;
    }

    /// <summary>
    /// Recherche le certificat de signature dans le store CurrentUser\My.
    /// </summary>
    private X509Certificate2? FindSigningCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var certs = store.Certificates.Find(
            X509FindType.FindBySubjectDistinguishedName,
            CertificateSubject,
            validOnly: false);

        store.Close();

        // Prendre le plus récent si plusieurs
        return certs
            .OrderByDescending(c => c.NotBefore)
            .FirstOrDefault();
    }

    /// <summary>
    /// Construit le payload à signer :
    /// "{InstallerHash}|{Name}|{Version}|{Arguments}|{Type}|{InstallerPath}|{StepsJson}"
    /// VULN-02 : inclut tous les champs exécutables pour empêcher la substitution post-signature.
    /// </summary>
    private static byte[] BuildSignaturePayload(DeploymentPackage package)
    {
        // Sérialiser les steps de manière déterministe (trié par Order)
        var stepsJson = package.Steps.Count > 0
            ? JsonSerializer.Serialize(
                package.Steps.OrderBy(s => s.Order).Select(s => new { s.Order, s.Type, s.Action }),
                new JsonSerializerOptions { WriteIndented = false })
            : "[]";

        var payload = string.Join("|",
            package.Installer.InstallerHash,
            package.Name,
            package.Version,
            package.Installer.Arguments,
            package.Installer.Type,
            package.Installer.Path,
            stepsJson);

        return Encoding.UTF8.GetBytes(payload);
    }
}
