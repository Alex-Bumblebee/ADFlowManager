using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models.Deployment;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace ADFlowManager.Infrastructure.Services;

/// <summary>
/// Service de déploiement de packages logiciels sur des ordinateurs distants.
/// Utilise l'approche SMB + SCM (comme PDQ Deploy) :
///   1. Copie des fichiers via partage admin$ (SMB, port 445)
///   2. Création d'un service Windows temporaire via SCM distant (RPC, port 135)
///   3. Le service exécute l'installeur en LOCAL SYSTEM
///   4. Monitoring via SCM + fichier log sur le partage
///   5. Nettoyage : suppression du service + fichiers
/// Aucune configuration requise sur le PC cible (pas de WinRM, pas de CIM/WMI).
/// </summary>
public class DeploymentService : IDeploymentService
{
    private readonly IAuditService _auditService;
    private readonly ICredentialService _credentialService;
    private readonly IPackageSigningService _signingService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<DeploymentService> _logger;

    private const string DeployFolder = @"ADFlowManager\Deploy";
    private const string ServicePrefix = "ADFlowDeploy_";

    /// <summary>
    /// Sémaphore global limitant le nombre total de déploiements simultanés
    /// (tous batchs confondus). Initialisé à partir de MaxGlobalDeployments dans les settings.
    /// Valeur par défaut : 15. Changement effectif au redémarrage de l'application.
    /// </summary>
    private static SemaphoreSlim? _globalDeploySemaphore;
    private static readonly object _semaphoreLock = new();

    /// <summary>
    /// Regex de validation des noms d'ordinateurs (NetBIOS ou FQDN).
    /// Bloque les path traversal et caractères spéciaux.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex ValidComputerNameRegex =
        new(@"^[a-zA-Z0-9][a-zA-Z0-9\-\.]{0,254}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Caractères dangereux dans un contexte batch CMD (injection de commandes).
    /// </summary>
    private static readonly char[] BatchDangerousChars = ['&', '|', '>', '<', '^', '%', '`', '\"', '\'', '\n', '\r'];

    public DeploymentService(
        IAuditService auditService,
        ICredentialService credentialService,
        IPackageSigningService signingService,
        ISettingsService settingsService,
        ILogger<DeploymentService> logger)
    {
        _auditService = auditService;
        _credentialService = credentialService;
        _signingService = signingService;
        _settingsService = settingsService;
        _logger = logger;

        // Initialiser le sémaphore global une seule fois (thread-safe)
        if (_globalDeploySemaphore == null)
        {
            lock (_semaphoreLock)
            {
                if (_globalDeploySemaphore == null)
                {
                    var maxGlobal = settingsService.CurrentSettings.Deployment.MaxGlobalDeployments;
                    if (maxGlobal < 1) maxGlobal = 15;
                    _globalDeploySemaphore = new SemaphoreSlim(maxGlobal, maxGlobal);
                    _logger.LogInformation("Sémaphore global de déploiement initialisé : max {Max} simultanés", maxGlobal);
                }
            }
        }
    }

    #region Win32 SMB Interop

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(
        string lpszUsername, string lpszDomain, string lpszPassword,
        int dwLogonType, int dwLogonProvider, out SafeAccessTokenHandle phToken);

    // Logon type: New credentials for network connections only
    // Le process local garde son identité, mais les connexions réseau utilisent les credentials fournis
    private const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
    private const int LOGON32_PROVIDER_WINNT50 = 3;

    #endregion

    #region Win32 Network Connection Interop (IPC$)

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    private const int RESOURCETYPE_ANY = 0;

    #endregion

    #region Win32 SCM Interop

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint dwAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(
        IntPtr hSCManager, string lpServiceName, string lpDisplayName,
        uint dwDesiredAccess, uint dwServiceType, uint dwStartType, uint dwErrorControl,
        string lpBinaryPathName, string? lpLoadOrderGroup, IntPtr lpdwTagId,
        string? lpDependencies, string? lpServiceStartName, string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr hService, uint dwControl, out SERVICE_STATUS lpServiceStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    // SCM Access Rights
    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    // Service types
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    // Start types
    private const uint SERVICE_DEMAND_START = 0x00000003;
    // Error control
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;
    // Service states
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint SERVICE_RUNNING = 0x00000004;
    // Control codes
    private const uint SERVICE_CONTROL_STOP = 0x00000001;

    #endregion

    /// <inheritdoc/>
    public async Task<DeploymentResult> DeployPackageAsync(
        string computerName,
        DeploymentPackage package,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // === Sémaphore global : limiter le nombre total de déploiements simultanés ===
        if (!await _globalDeploySemaphore!.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken))
        {
            return new DeploymentResult
            {
                ComputerName = computerName,
                PackageId = package.Id,
                PackageName = package.Name,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Success = false,
                ErrorMessage = "Timeout : trop de déploiements simultanés en cours. Réessayez plus tard."
            };
        }

        try
        {
            return await DeployPackageInternalAsync(computerName, package, progress, cancellationToken);
        }
        finally
        {
            _globalDeploySemaphore.Release();
        }
    }

    /// <summary>
    /// Implémentation interne du déploiement (après acquisition du sémaphore global).
    /// </summary>
    private async Task<DeploymentResult> DeployPackageInternalAsync(
        string computerName,
        DeploymentPackage package,
        IProgress<DeploymentProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new DeploymentResult
        {
            ComputerName = computerName,
            PackageId = package.Id,
            PackageName = package.Name,
            PackageVersion = package.Version,
            StartTime = DateTime.UtcNow
        };

        var serviceName = $"{ServicePrefix}{Guid.NewGuid():N}"[..40];
        // Chemins distants (partage admin$) et locaux (sur le PC cible)
        var remoteSharePath = $@"\\{computerName}\admin$\{DeployFolder}";
        var localPathOnTarget = $@"C:\Windows\{DeployFolder}";
        var ipcPath = $@"\\{computerName}\IPC$";
        var adminPath = $@"\\{computerName}\admin$";
        var ipcConnected = false;
        var adminConnected = false;
        ImpersonationContext? impersonation = null;

        try
        {
            // Validation du nom d'ordinateur (anti path-traversal)
            if (!ValidComputerNameRegex.IsMatch(computerName))
                throw new ArgumentException(
                    $"Nom d'ordinateur invalide : '{computerName}'. Seuls les caractères alphanumériques, tirets et points sont autorisés.");

            _logger.LogInformation("Déploiement SMB+SCM {Package} v{Version} sur {Computer}...",
                package.Name, package.Version, computerName);

            // Charger les credentials AD
            var (domain, username, password) = _credentialService.LoadSessionCredentials();
            if (string.IsNullOrEmpty(username))
                (domain, username, password) = _credentialService.LoadCredentials();

            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException(
                    "Aucun credential AD configuré. Connectez-vous d'abord via les paramètres de connexion.");

            var fullUser = $@"{domain}\{username}";

            // === Connexions réseau authentifiées (approche PSExec) ===
            // IPC$ : tunnel RPC pour SCM distant (création/démarrage service)
            // admin$ : partage SMB pour copie fichiers
            // Les deux sont ouverts avec credentials explicites dans la même session de logon
            progress?.Report(new DeploymentProgress
            {
                Stage = "Connexion",
                Percentage = 5,
                Message = $"Connexion à {computerName}..."
            });

            var nrIpc = new NETRESOURCE { dwType = RESOURCETYPE_ANY, lpRemoteName = ipcPath };
            var ipcResult = WNetAddConnection2(ref nrIpc, password, fullUser, 0);
            if (ipcResult != 0 && ipcResult != 1219) // 1219 = already connected
                throw new Win32Exception(ipcResult,
                    $"Impossible d'établir la connexion IPC$ vers {computerName} (erreur {ipcResult}). " +
                    "Vérifiez les credentials et la connectivité réseau.");
            ipcConnected = true;
            _logger.LogDebug("Connexion IPC$ établie vers {Computer}", computerName);

            var nrAdmin = new NETRESOURCE { dwType = RESOURCETYPE_ANY, lpRemoteName = adminPath };
            var adminResult = WNetAddConnection2(ref nrAdmin, password, fullUser, 0);
            if (adminResult != 0 && adminResult != 1219)
                throw new Win32Exception(adminResult,
                    $"Impossible d'établir la connexion admin$ vers {computerName} (erreur {adminResult}). " +
                    "Vérifiez que le partage admin$ est actif et les droits administrateur local.");
            adminConnected = true;
            _logger.LogDebug("Connexion admin$ établie vers {Computer}", computerName);

            // === Étape 1 : Vérification hash + Copie fichiers via SMB (admin$ déjà connecté) ===
            progress?.Report(new DeploymentProgress
            {
                Stage = "Vérification",
                Percentage = 10,
                Message = "Vérification de l'intégrité de l'installeur..."
            });

            await Task.Run(() =>
            {
                // === Vérification SHA-256 avant copie ===
                if (!string.IsNullOrWhiteSpace(package.Installer.InstallerHash))
                {
                    using var stream = File.OpenRead(package.Installer.Path);
                    var hashBytes = SHA256.HashData(stream);
                    var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                    var expectedHash = package.Installer.InstallerHash.ToLowerInvariant();

                    if (actualHash != expectedHash)
                    {
                        _logger.LogError(
                            "Hash SHA-256 incorrect pour {Package} : attendu {Expected}, obtenu {Actual}",
                            package.Name, expectedHash, actualHash);
                        throw new InvalidOperationException(
                            $"Intégrité de l'installeur compromise ! " +
                            $"Hash attendu : {expectedHash[..16]}... — Hash obtenu : {actualHash[..16]}...");
                    }

                    _logger.LogDebug("Hash SHA-256 vérifié pour {Package}", package.Name);
                }

                // === Vérification signature ECDSA après hash ===
                if (!string.IsNullOrWhiteSpace(package.Signature))
                {
                    if (!_signingService.VerifyPackage(package))
                    {
                        throw new InvalidOperationException(
                            $"Signature ECDSA invalide pour le package '{package.Name}' v{package.Version} ! " +
                            "Le package a peut-être été altéré ou signé avec une autre clé.");
                    }
                    _logger.LogDebug("Signature ECDSA vérifiée pour {Package}", package.Name);
                }
                else
                {
                    // VULN-01 : bloquer si RequireSignedPackages est activé
                    if (_settingsService.CurrentSettings.Deployment.RequireSignedPackages)
                    {
                        throw new InvalidOperationException(
                            $"Le package '{package.Name}' v{package.Version} n'est pas signé. " +
                            "Le déploiement de packages non signés est interdit (RequireSignedPackages = true).");
                    }

                    _logger.LogWarning(
                        "Le package '{Name}' v{Version} n'est pas signé. Déploiement autorisé mais non vérifié.",
                        package.Name, package.Version);
                }

                Directory.CreateDirectory(remoteSharePath);

                var installerName = Path.GetFileName(package.Installer.Path);
                var remoteDest = Path.Combine(remoteSharePath, installerName);
                try
                {
                    File.Copy(package.Installer.Path, remoteDest, overwrite: true);
                }
                catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
                {
                    throw new IOException(
                        $"Le fichier installeur est déjà présent et verrouillé sur {computerName} — "
                        + $"une installation précédente est probablement encore en cours ou incomplète. "
                        + $"Nettoyez manuellement C:\\Windows\\{DeployFolder} sur {computerName} puis réessayez.",
                        ioEx);
                }

                var commandLine = BuildInstallerCommand(package, localPathOnTarget, installerName);

                // Sanitize : supprimer les caractères dangereux pour cmd.exe
                var safeName = SanitizeBatchString(package.Name);
                var safeVersion = SanitizeBatchString(package.Version);
                var logFile = $"{serviceName}.log";

                var batchContent = $"""
                    @echo off
                    echo [%DATE% %TIME%] Debut installation {safeName} v{safeVersion} >> "{localPathOnTarget}\{logFile}"
                    eventcreate /L Application /T Information /SO ADFlowManager /ID 1000 /D "Debut installation : {safeName} v{safeVersion}" >nul 2>&1
                    {commandLine}
                    set EXIT_CODE=%ERRORLEVEL%
                    echo %EXIT_CODE% > "{localPathOnTarget}\{serviceName}.exitcode"
                    if %EXIT_CODE%==0 (
                        eventcreate /L Application /T Success /SO ADFlowManager /ID 1001 /D "Installation reussie : {safeName} v{safeVersion} (code: %EXIT_CODE%)" >nul 2>&1
                    ) else (
                        eventcreate /L Application /T Warning /SO ADFlowManager /ID 1002 /D "Installation terminee avec erreur : {safeName} v{safeVersion} (code: %EXIT_CODE%)" >nul 2>&1
                    )
                    echo [%DATE% %TIME%] Fin installation (code: %EXIT_CODE%) >> "{localPathOnTarget}\{logFile}"
                    exit /b 0
                    """;

                File.WriteAllText(Path.Combine(remoteSharePath, $"{serviceName}.cmd"), batchContent, Encoding.ASCII);
                _logger.LogDebug("Fichiers copiés vers {Path}", remoteSharePath);
            }, cancellationToken);

            // === Étape 2 : Création + démarrage du service via P/Invoke SCM sous RunImpersonated ===
            // RunImpersonated utilise le même token que LogonUser → même session de logon que WNet IPC$
            // Donc OpenSCManager (appel RPC) voit la connexion IPC$ authentifiée
            // Token créé juste avant les appels SCM et disposé dès que possible (minimise fenêtre Mimikatz)
            progress?.Report(new DeploymentProgress
            {
                Stage = "Installation",
                Percentage = 30,
                Message = "Création du service de déploiement distant..."
            });

            var batchPath = $@"{localPathOnTarget}\{serviceName}.cmd";
            var binPath = $@"cmd.exe /c ""{batchPath}""";

            impersonation = CreateImpersonationContext();

            await Task.Run(() =>
            {
                WindowsIdentity.RunImpersonated(impersonation.Token, () =>
                {
                    var hScm = OpenSCManager(computerName, null, SC_MANAGER_ALL_ACCESS);
                    if (hScm == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error(),
                            $"Impossible d'ouvrir le Service Control Manager sur {computerName}. " +
                            "Vérifiez les droits administrateur et la connectivité RPC (port 135).");

                    try
                    {
                        var hService = CreateService(
                            hScm, serviceName,
                            $"ADFlowManager Deploy - {SanitizeBatchString(package.Name)}",
                            SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS,
                            SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL,
                            binPath, null, IntPtr.Zero, null, null, null);

                        if (hService == IntPtr.Zero)
                            throw new Win32Exception(Marshal.GetLastWin32Error(),
                                "Impossible de créer le service de déploiement distant.");

                        try
                        {
                            if (!StartService(hService, 0, IntPtr.Zero))
                            {
                                var err = Marshal.GetLastWin32Error();
                                // 1053 = service didn't respond in time — normal pour cmd.exe qui se termine vite
                                if (err != 1053)
                                    throw new Win32Exception(err, "Impossible de démarrer le service de déploiement.");
                            }
                        }
                        finally
                        {
                            CloseServiceHandle(hService);
                        }
                    }
                    finally
                    {
                        CloseServiceHandle(hScm);
                    }
                });
            }, cancellationToken);

            // Disposer le token d'impersonation immédiatement après les appels SCM
            // Le service est déjà démarré, on n'a plus besoin du token réseau
            impersonation?.Dispose();
            impersonation = null;
            _logger.LogDebug("Token d'impersonation disposé (minimisation Mimikatz)");

            // === Étape 3 : Attente fin d'exécution (polling fichier .exitcode via SMB) ===
            progress?.Report(new DeploymentProgress
            {
                Stage = "En cours",
                Percentage = 50,
                Message = "Installation en cours..."
            });

            var exitCodeFile = Path.Combine(remoteSharePath, $"{serviceName}.exitcode");
            var timeoutSeconds = CalculateTimeout(package);
            result.TimeoutUsedSeconds = timeoutSeconds;
            var stopwatch = Stopwatch.StartNew();
            int? exitCode = null;

            while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // admin$ est déjà connecté via WNet, donc File.Exists fonctionne directement
                if (File.Exists(exitCodeFile))
                {
                    var content = File.ReadAllText(exitCodeFile).Trim();
                    if (int.TryParse(content, out var code))
                    {
                        exitCode = code;
                        break;
                    }
                }

                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var pct = 50 + (int)(30 * elapsed / timeoutSeconds);
                progress?.Report(new DeploymentProgress
                {
                    Stage = "En cours",
                    Percentage = Math.Min(pct, 80),
                    Message = $"Installation en cours ({(int)elapsed}s)..."
                });

                await Task.Delay(2000, cancellationToken);
            }

            if (exitCode == null)
            {
                result.IsTimeout = true;

                // Détecter si l'installeur était encore actif au moment du timeout :
                // si le fichier est verrouillé par un autre processus, l'installation est probablement encore en cours.
                var installerRemotePath = Path.Combine(remoteSharePath, Path.GetFileName(package.Installer.Path));
                var installerStillRunning = false;
                try
                {
                    using var probe = File.Open(installerRemotePath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException)
                {
                    installerStillRunning = true; // fichier verrouillé = processus encore actif
                }
                catch { /* fichier déjà supprimé ou inaccessible — on ne peut pas conclure */ }

                result.InstallerWasRunning = installerStillRunning;

                var message = installerStillRunning
                    ? $"Timeout ({timeoutSeconds}s) dépassé mais l'installeur était encore actif sur {computerName}. " +
                      $"L'installation a probablement réussi. Vérifiez manuellement et envisagez d'augmenter le timeout."
                    : $"Timeout : l'installation n'a pas terminé après {timeoutSeconds}s.";

                _logger.LogWarning(
                    "Timeout {Package} sur {Computer} après {Timeout}s (installeur encore actif : {Running})",
                    package.Name, computerName, timeoutSeconds, installerStillRunning);

                throw new TimeoutException(message);
            }

            if (exitCode != 0 && exitCode != 3010)
                throw new Exception(
                    $"L'installeur a retourné le code d'erreur {exitCode}.");

            if (exitCode == 3010)
            {
                _logger.LogWarning("Installation {Package} sur {Computer} réussie, redémarrage requis (code 3010)",
                    package.Name, computerName);
            }

            // === Étape 4 : Vérification critère de succès ===
            progress?.Report(new DeploymentProgress
            {
                Stage = "Vérification",
                Percentage = 85,
                Message = "Vérification de l'installation..."
            });

            if (package.SuccessCriteria != null)
            {
                var verified = VerifyInstallation(computerName, package.SuccessCriteria);
                if (!verified)
                    throw new Exception("Critère de succès non vérifié après installation.");
            }

            // === Étape 5 : Nettoyage (service P/Invoke sous RunImpersonated + fichiers via SMB) ===
            progress?.Report(new DeploymentProgress
            {
                Stage = "Nettoyage",
                Percentage = 92,
                Message = "Nettoyage du service et fichiers temporaires..."
            });

            // Token frais pour le nettoyage SCM (le précédent a été disposé)
            using (var cleanupCtx = CreateImpersonationContext())
            {
                CleanupRemoteService(computerName, serviceName, cleanupCtx.Token);
            }
            // Les fichiers sont nettoyés dans le finally (avant déconnexion admin$)

            progress?.Report(new DeploymentProgress
            {
                Stage = "Terminé",
                Percentage = 100,
                Message = exitCode == 3010
                    ? "Déploiement réussi (redémarrage requis)"
                    : "Déploiement réussi"
            });

            _logger.LogInformation("Déploiement {Package} sur {Computer} réussi (exit code: {ExitCode})",
                package.Name, computerName, exitCode);

            result.Success = true;
            result.ExitCode = exitCode.Value;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorCategory = CategorizeException(ex);

            // Raffiner si le timeout avait déjà détecté l'installeur actif
            if (result.IsTimeout && result.InstallerWasRunning)
                result.ErrorCategory = DeploymentErrorCategory.TimeoutProbablyOk;

            _logger.LogError(ex, "Échec déploiement {Package} sur {Computer}", package.Name, computerName);

            progress?.Report(new DeploymentProgress
            {
                Stage = "Erreur",
                Percentage = 0,
                Message = $"Erreur: {ex.Message}"
            });

            // Tenter le nettoyage du service même en cas d'erreur
            try
            {
                using var ctx = CreateImpersonationContext();
                CleanupRemoteService(computerName, serviceName, ctx.Token);
            }
            catch { }

            // Le nettoyage des fichiers est dans le finally (avant déconnexion admin$)
        }
        finally
        {
            // Disposer le token si encore actif (sécurité — normalement déjà disposé)
            impersonation?.Dispose();

            // Nettoyage des fichiers résiduels AVANT déconnexion admin$
            // (après WNetCancelConnection2, le partage n'est plus accessible)
            if (adminConnected)
            {
                result.ResidualFiles = CleanupRemoteFiles(remoteSharePath, serviceName, package, computerName);
            }

            // Déconnecter IPC$ et admin$
            if (adminConnected)
            {
                try { WNetCancelConnection2(adminPath, 0, true); } catch { }
                _logger.LogDebug("Connexion admin$ vers {Computer} fermée", computerName);
            }
            if (ipcConnected)
            {
                try { WNetCancelConnection2(ipcPath, 0, true); } catch { }
                _logger.LogDebug("Connexion IPC$ vers {Computer} fermée", computerName);
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;

            await _auditService.LogAsync(
                actionType: "DeployPackage",
                entityType: "Computer",
                entityId: computerName,
                entityDisplayName: computerName,
                details: new { Package = package.Name, Version = package.Version, Duration = result.Duration.TotalSeconds },
                success: result.Success,
                errorMessage: result.ErrorMessage);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<DeploymentResult>> DeployPackageBatchAsync(
        IEnumerable<string> computerNames,
        DeploymentPackage package,
        int maxParallel = 5,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DeploymentResult>();
        var computers = computerNames.ToList();
        var totalCount = computers.Count;
        var completedCount = 0;

        _logger.LogInformation("Déploiement batch {Package} sur {Count} ordinateurs (max {MaxParallel} en parallèle)",
            package.Name, totalCount, maxParallel);

        var semaphore = new SemaphoreSlim(maxParallel);
        var tasks = new List<Task<DeploymentResult>>();

        foreach (var computer in computers)
        {
            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    var computerProgress = new Progress<DeploymentProgress>(p =>
                    {
                        var overallPercentage = (completedCount * 100 + p.Percentage) / totalCount;
                        progress?.Report(new DeploymentProgress
                        {
                            Stage = computer,
                            Percentage = overallPercentage,
                            Message = p.Message
                        });
                    });

                    return await DeployPackageAsync(computer, package, computerProgress, cancellationToken);
                }
                finally
                {
                    Interlocked.Increment(ref completedCount);
                    semaphore.Release();
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        results.AddRange(await Task.WhenAll(tasks));

        var successCount = results.Count(r => r.Success);
        _logger.LogInformation("Déploiement batch terminé : {Success}/{Total} réussis",
            successCount, totalCount);

        return results;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<DeploymentResult>> DeployMultiplePackagesBatchAsync(
        IEnumerable<string> computerNames,
        IEnumerable<DeploymentPackage> packages,
        int maxParallel = 5,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var allResults = new List<DeploymentResult>();
        var computerList = computerNames.ToList();
        var packageList = packages.ToList();
        var totalPackages = packageList.Count;

        _logger.LogInformation(
            "Déploiement multiple : {PackageCount} package(s) sur {ComputerCount} ordinateurs",
            totalPackages, computerList.Count);

        for (var i = 0; i < packageList.Count; i++)
        {
            var pkg = packageList[i];
            var pkgIndex = i;

            var packageProgress = new Progress<DeploymentProgress>(p =>
            {
                var overallPct = (pkgIndex * 100 + p.Percentage) / totalPackages;
                progress?.Report(new DeploymentProgress
                {
                    Stage = $"[{pkgIndex + 1}/{totalPackages}] {p.Stage}",
                    Percentage = overallPct,
                    Message = p.Message
                });
            });

            var batchResults = await DeployPackageBatchAsync(
                computerList, pkg, maxParallel, packageProgress, cancellationToken);

            allResults.AddRange(batchResults);
        }

        var successCount = allResults.Count(r => r.Success);
        _logger.LogInformation(
            "Déploiement multiple terminé : {Success}/{Total} réussis ({Packages} packages × {Computers} PC)",
            successCount, allResults.Count, totalPackages, computerList.Count);

        return allResults;
    }

    /// <summary>
    /// Classe l'exception d'un déploiement en catégorie d'erreur actionnable.
    /// </summary>
    private static DeploymentErrorCategory CategorizeException(Exception ex) => ex switch
    {
        TimeoutException                                                    => DeploymentErrorCategory.Timeout,
        IOException e when e.Message.Contains("verrouillé")               => DeploymentErrorCategory.FileLocked,
        IOException                                                         => DeploymentErrorCategory.Other,
        System.ComponentModel.Win32Exception { NativeErrorCode: 53 or 5 or 1326 }
                                                                           => DeploymentErrorCategory.ConnectionError,
        InvalidOperationException e when e.Message.Contains("Intégrité") ||
                                         e.Message.Contains("ECDSA")      => DeploymentErrorCategory.IntegrityError,
        Exception e when e.Message.StartsWith("L'installeur a retourné")  => DeploymentErrorCategory.InstallerNonZeroExit,
        _                                                                   => DeploymentErrorCategory.Other
    };

    /// <summary>
    /// Supprime les caractères dangereux pour un contexte batch CMD.
    /// Empêche l'injection de commandes via &, |, >, <, ^, %, etc.
    /// </summary>
    private static string SanitizeBatchString(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sanitized = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (Array.IndexOf(BatchDangerousChars, c) < 0)
                sanitized.Append(c);
        }
        return sanitized.ToString();
    }

    /// <summary>
    /// Construit la ligne de commande d'installation selon le type d'installeur.
    /// Les arguments sont sanitisés pour empêcher l'injection de commandes batch.
    /// </summary>
    private static string BuildInstallerCommand(DeploymentPackage package, string localPath, string installerName)
    {
        var installerFullPath = $@"{localPath}\{installerName}";
        var safeArgs = SanitizeBatchString(package.Installer.Arguments);

        return package.Installer.Type.ToLower() switch
        {
            "exe" => $@"""{installerFullPath}"" {safeArgs}",
            "msi" => $@"msiexec.exe /i ""{installerFullPath}"" {safeArgs}",
            "ps1" => $@"powershell.exe -ExecutionPolicy Bypass -NoProfile -File ""{installerFullPath}""",
            _ => throw new NotSupportedException($"Type installateur non supporté: {package.Installer.Type}")
        };
    }

    /// <summary>
    /// Calcule le timeout d'installation.
    /// Si TimeoutSeconds > 0 dans le package = valeur manuelle (prioritaire).
    /// Si TimeoutSeconds == 0 = calcul dynamique basé sur la taille du fichier installeur.
    /// </summary>
    private int CalculateTimeout(DeploymentPackage package)
    {
        // Timeout manuel prioritaire
        if (package.Installer.TimeoutSeconds > 0)
            return package.Installer.TimeoutSeconds;

        // Calcul dynamique basé sur la taille du fichier
        try
        {
            var fileInfo = new FileInfo(package.Installer.Path);
            var sizeMb = fileInfo.Length / (1024.0 * 1024.0);

            var timeout = sizeMb switch
            {
                < 10 => 120,    // < 10 Mo → 2 min
                < 50 => 150,    // < 50 Mo → 2.5 min
                < 200 => 300,   // < 200 Mo → 5 min
                < 500 => 600,   // < 500 Mo → 10 min
                _ => 900        // > 500 Mo → 15 min
            };

            _logger.LogDebug("Timeout dynamique pour {Package} : {Size:F1} Mo → {Timeout}s",
                package.Name, sizeMb, timeout);

            return timeout;
        }
        catch
        {
            return 300; // Fallback 5 min si le fichier n'est pas accessible
        }
    }

    /// <summary>
    /// Supprime le service temporaire distant via P/Invoke SCM (sous RunImpersonated).
    /// Doit être appelé pendant que la connexion IPC$ est active.
    /// </summary>
    private void CleanupRemoteService(string computerName, string serviceName, SafeAccessTokenHandle token)
    {
        try
        {
            WindowsIdentity.RunImpersonated(token, () =>
            {
                var hScm = OpenSCManager(computerName, null, SC_MANAGER_ALL_ACCESS);
                if (hScm == IntPtr.Zero) return;

                try
                {
                    var hService = OpenService(hScm, serviceName, SERVICE_ALL_ACCESS);
                    if (hService == IntPtr.Zero) return;

                    try
                    {
                        // Arrêter le service s'il tourne encore
                        if (QueryServiceStatus(hService, out var status) && status.dwCurrentState == SERVICE_RUNNING)
                        {
                            ControlService(hService, SERVICE_CONTROL_STOP, out _);
                        }

                        DeleteService(hService);
                        _logger.LogDebug("Service {Service} supprimé de {Computer}", serviceName, computerName);
                    }
                    finally
                    {
                        CloseServiceHandle(hService);
                    }
                }
                finally
                {
                    CloseServiceHandle(hScm);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur nettoyage service distant {Service}", serviceName);
        }
    }

    /// <summary>
    /// Nettoie les fichiers temporaires de déploiement sur le partage distant.
    /// Doit être appelé AVANT WNetCancelConnection2 (sinon le partage n'est plus accessible).
    /// Chaque fichier est supprimé individuellement ; en cas d'échec, un warning est loggé
    /// avec le chemin résiduel pour permettre un nettoyage manuel.
    /// </summary>
    private List<string> CleanupRemoteFiles(string remoteSharePath, string serviceName, DeploymentPackage package, string computerName)
    {
        var installerName = Path.GetFileName(package.Installer.Path);
        var filesToDelete = new[]
        {
            Path.Combine(remoteSharePath, installerName),
            Path.Combine(remoteSharePath, $"{serviceName}.cmd"),
            Path.Combine(remoteSharePath, $"{serviceName}.exitcode"),
            Path.Combine(remoteSharePath, $"{serviceName}.log")
        };

        var residualFiles = new List<string>();

        foreach (var file in filesToDelete)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                    _logger.LogDebug("Fichier supprimé : {File}", file);
                }
            }
            catch (Exception ex)
            {
                residualFiles.Add(Path.GetFileName(file));
                _logger.LogWarning(ex, "Impossible de supprimer {File} sur {Computer} — fichier résiduel",
                    file, computerName);
            }
        }

        // Tenter de supprimer le dossier si vide
        try
        {
            if (Directory.Exists(remoteSharePath) && !Directory.EnumerateFileSystemEntries(remoteSharePath).Any())
            {
                Directory.Delete(remoteSharePath);
                _logger.LogDebug("Dossier de déploiement supprimé : {Path}", remoteSharePath);
            }
            else if (Directory.Exists(remoteSharePath))
            {
                var remaining = Directory.EnumerateFileSystemEntries(remoteSharePath).Select(Path.GetFileName).ToList();
                _logger.LogWarning(
                    "Dossier résiduel sur {Computer} : {Path} — fichiers restants : {Files}",
                    computerName, remoteSharePath, string.Join(", ", remaining));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de nettoyer le dossier {Path} sur {Computer}",
                remoteSharePath, computerName);
        }

        if (residualFiles.Count > 0)
        {
            _logger.LogWarning(
                "⚠ {Count} fichier(s) résiduel(s) sur {Computer} dans {Path} : {Files}. Nettoyage manuel requis.",
                residualFiles.Count, computerName, $@"C:\Windows\{DeployFolder}",
                string.Join(", ", residualFiles));
        }

        return residualFiles;
    }

    /// <summary>
    /// Vérifie le critère de succès post-installation (synchrone, doit être appelé sous impersonation).
    /// </summary>
    private bool VerifyInstallation(string computerName, SuccessCriteria criteria)
    {
        try
        {
            if (criteria.Type == "fileExists")
            {
                var remotePath = criteria.Path.Replace(":", "$");
                var fullPath = $@"\\{computerName}\{remotePath}";
                return File.Exists(fullPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur vérification critère succès sur {Computer}", computerName);
            return false;
        }
    }

    #region Impersonation Helper

    /// <summary>
    /// Crée un contexte d'impersonation réseau avec les credentials AD stockés.
    /// Utilise LOGON32_LOGON_NEW_CREDENTIALS qui :
    ///   - Ne nécessite PAS d'élévation (pas besoin de lancer l'app en admin)
    ///   - N'affecte que les connexions réseau (SMB, RPC/SCM)
    ///   - Le processus local garde son identité courante
    /// </summary>
    private ImpersonationContext CreateImpersonationContext()
    {
        // Charger les credentials AD (session d'abord, puis persistés)
        var (domain, username, password) = _credentialService.LoadSessionCredentials();
        if (string.IsNullOrEmpty(username))
        {
            (domain, username, password) = _credentialService.LoadCredentials();
        }

        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "Aucun credential AD configuré. Connectez-vous d'abord via les paramètres de connexion.");
        }

        _logger.LogDebug("Impersonation réseau avec {Domain}\\{User}", domain, username);

        if (!LogonUser(username, domain, password,
                LOGON32_LOGON_NEW_CREDENTIALS, LOGON32_PROVIDER_WINNT50,
                out var tokenHandle))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error,
                $"Impossible d'authentifier {domain}\\{username} (erreur Win32: {error}). " +
                "Vérifiez les credentials dans les paramètres de connexion.");
        }

        return new ImpersonationContext(tokenHandle);
    }

    /// <summary>
    /// Wrapper IDisposable pour le token d'impersonation.
    /// </summary>
    private sealed class ImpersonationContext : IDisposable
    {
        public SafeAccessTokenHandle Token { get; }

        public ImpersonationContext(SafeAccessTokenHandle token)
        {
            Token = token;
        }

        public void Dispose()
        {
            Token.Dispose();
        }
    }

    #endregion
}
