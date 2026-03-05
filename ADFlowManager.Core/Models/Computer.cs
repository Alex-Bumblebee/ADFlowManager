namespace ADFlowManager.Core.Models;

/// <summary>
/// Représente un ordinateur du domaine Active Directory.
/// </summary>
public class Computer
{
    /// <summary>
    /// Nom de l'ordinateur (CN).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Distinguished Name complet dans AD.
    /// </summary>
    public string DistinguishedName { get; set; } = string.Empty;

    /// <summary>
    /// Description de l'ordinateur.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Système d'exploitation (ex: Windows 11 Enterprise).
    /// </summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>
    /// Version du système d'exploitation.
    /// </summary>
    public string OperatingSystemVersion { get; set; } = string.Empty;

    /// <summary>
    /// Dernière connexion au domaine.
    /// </summary>
    public DateTime? LastLogon { get; set; }

    /// <summary>
    /// Indique si le compte ordinateur est activé.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Emplacement (attribut location).
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Géré par (attribut managedBy).
    /// </summary>
    public string ManagedBy { get; set; } = string.Empty;

    /// <summary>
    /// Groupes dont l'ordinateur est membre.
    /// </summary>
    public List<string> MemberOf { get; set; } = new();

    /// <summary>
    /// Date de création dans AD.
    /// </summary>
    public DateTime Created { get; set; }

    /// <summary>
    /// Date de dernière modification.
    /// </summary>
    public DateTime Modified { get; set; }

    /// <summary>
    /// Adresse IPv4 de l'ordinateur.
    /// </summary>
    public string IPv4Address { get; set; } = string.Empty;

    /// <summary>
    /// Adresse MAC de l'ordinateur.
    /// </summary>
    public string MACAddress { get; set; } = string.Empty;

    /// <summary>
    /// Indique si l'ordinateur est en ligne (ping).
    /// </summary>
    public bool IsOnline { get; set; }
}

/// <summary>
/// Informations système d'un ordinateur distant.
/// </summary>
public class ComputerSystemInfo
{
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int TotalMemoryMB { get; set; }
    public int DiskSpaceGB { get; set; }
    public string ProcessorName { get; set; } = string.Empty;
    public int ProcessorCores { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
}
