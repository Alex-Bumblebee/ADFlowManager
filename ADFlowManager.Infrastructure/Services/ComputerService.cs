using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Net.NetworkInformation;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace ADFlowManager.Infrastructure.Services;

/// <summary>
/// Service de gestion des ordinateurs Active Directory.
/// Utilise System.DirectoryServices.AccountManagement pour interroger AD.
/// </summary>
public class ComputerService : IComputerService
{
    private readonly IActiveDirectoryService _adService;
    private readonly ILogger<ComputerService> _logger;

    public ComputerService(
        IActiveDirectoryService adService,
        ILogger<ComputerService> logger)
    {
        _adService = adService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Computer>> GetComputersAsync(string? searchFilter = null)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Récupération des ordinateurs AD...");

            var computers = new List<Computer>();

            try
            {
                using var context = new PrincipalContext(ContextType.Domain);
                using var computerPrincipal = new ComputerPrincipal(context);

                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    computerPrincipal.Name = $"*{searchFilter}*";
                }

                using var searcher = new PrincipalSearcher(computerPrincipal);

                foreach (var result in searcher.FindAll())
                {
                    if (result is not ComputerPrincipal comp) continue;

                    try
                    {
                        var computer = new Computer
                        {
                            Name = comp.Name ?? string.Empty,
                            DistinguishedName = comp.DistinguishedName ?? string.Empty,
                            Description = comp.Description ?? string.Empty,
                            Enabled = comp.Enabled ?? true,
                            LastLogon = comp.LastLogon
                        };

                        // Propriétés avancées via DirectoryEntry
                        if (comp.GetUnderlyingObject() is DirectoryEntry entry)
                        {
                            computer.OperatingSystem = entry.Properties["operatingSystem"]?.Value?.ToString() ?? string.Empty;
                            computer.OperatingSystemVersion = entry.Properties["operatingSystemVersion"]?.Value?.ToString() ?? string.Empty;
                            computer.Location = entry.Properties["location"]?.Value?.ToString() ?? string.Empty;
                            computer.ManagedBy = entry.Properties["managedBy"]?.Value?.ToString() ?? string.Empty;
                            computer.Created = entry.Properties["whenCreated"]?.Value is DateTime created ? created : DateTime.MinValue;
                            computer.Modified = entry.Properties["whenChanged"]?.Value is DateTime modified ? modified : DateTime.MinValue;
                        }

                        computers.Add(computer);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erreur lecture ordinateur {Name}", comp.Name);
                    }
                }

                _logger.LogInformation("{Count} ordinateurs récupérés depuis AD", computers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des ordinateurs AD");
            }

            return computers;
        });
    }

    /// <inheritdoc/>
    public async Task<Computer?> GetComputerByNameAsync(string computerName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var context = new PrincipalContext(ContextType.Domain);
                using var comp = ComputerPrincipal.FindByIdentity(context, computerName);

                if (comp == null)
                    return null;

                var computer = new Computer
                {
                    Name = comp.Name ?? string.Empty,
                    DistinguishedName = comp.DistinguishedName ?? string.Empty,
                    Description = comp.Description ?? string.Empty,
                    Enabled = comp.Enabled ?? true,
                    LastLogon = comp.LastLogon
                };

                if (comp.GetUnderlyingObject() is DirectoryEntry entry)
                {
                    computer.OperatingSystem = entry.Properties["operatingSystem"]?.Value?.ToString() ?? string.Empty;
                    computer.OperatingSystemVersion = entry.Properties["operatingSystemVersion"]?.Value?.ToString() ?? string.Empty;
                    computer.Location = entry.Properties["location"]?.Value?.ToString() ?? string.Empty;
                    computer.ManagedBy = entry.Properties["managedBy"]?.Value?.ToString() ?? string.Empty;
                    computer.Created = entry.Properties["whenCreated"]?.Value is DateTime created ? created : DateTime.MinValue;
                    computer.Modified = entry.Properties["whenChanged"]?.Value is DateTime modified ? modified : DateTime.MinValue;
                }

                return computer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur récupération ordinateur {Name}", computerName);
                return null;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<bool> IsComputerOnlineAsync(string computerName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var ping = new Ping();
                var reply = ping.Send(computerName, timeout: 2000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<ComputerSystemInfo?> GetSystemInfoAsync(string computerName)
    {
        // Implémentation via CIM si besoin (future extension)
        return await Task.FromResult<ComputerSystemInfo?>(null);
    }
}
