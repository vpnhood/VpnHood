using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Server.Access;

/// <summary>
/// Represents what a VPN server is and has, as it sends it to its access manager to get its configuration
/// (<see cref="Managers.IAccessManager.Server_Configure"/>).
/// </summary>
public class ServerInfo
{
    /// <summary>Gets or sets the server's version.</summary>
    [JsonConverter(typeof(VersionConverter))]
    public required Version Version { get; set; }

    /// <summary>Gets or sets the version of the .NET runtime the server runs on.</summary>
    [JsonConverter(typeof(VersionConverter))]
    public required Version EnvironmentVersion { get; set; }

    /// <summary>Gets or sets the machine's local addresses, as the server finds them.</summary>
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] PrivateIpAddresses { get; set; }

    /// <summary>Gets or sets the machine's public addresses, where the server looks them up; otherwise, none.</summary>
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] PublicIpAddresses { get; set; }

    /// <summary>Gets or sets the server's status at the time.</summary>
    public required ServerStatus Status { get; set; }

    /// <summary>Gets or sets a description of the machine and its OS, or <see langword="null"/>.</summary>
    public string? OsInfo { get; set; }

    /// <summary>Gets or sets the OS's version, or <see langword="null"/>.</summary>
    public string? OsVersion { get; set; }

    /// <summary>Gets or sets the machine's memory, in bytes, or <see langword="null"/> where not known.</summary>
    public long? TotalMemory { get; set; }

    /// <summary>Gets or sets the machine's name, or <see langword="null"/>.</summary>
    public string? MachineName { get; set; }

    /// <summary>Gets or sets the machine's logical cores.</summary>
    public int LogicalCoreCount { get; set; }

    /// <summary>
    /// Gets or sets a UDP port that was free over IPv4 when the server looked, for the tunnel's UDP channel, or 0 where
    /// it found none; nothing reserves it.
    /// </summary>
    public int FreeUdpPortV4 { get; set; }

    /// <summary>
    /// Gets or sets a UDP port that was free over IPv6, the IPv4 one where it is free there too, or 0 where the server
    /// found none; nothing reserves it.
    /// </summary>
    public int FreeUdpPortV6 { get; set; }

    /// <summary>
    /// Gets or sets a UDP port that was free over IPv4 for QUIC, 443 where it is, or 0 where the server found none;
    /// nothing reserves it.
    /// </summary>
    public int FreeQuicPortV4 { get; set; }

    /// <summary>
    /// Gets or sets a UDP port that was free over IPv6 for QUIC, 443 where it is, or 0 where the server found none;
    /// nothing reserves it.
    /// </summary>
    public int FreeQuicPortV6 { get; set; }

    /// <summary>
    /// Gets or sets the names of the machine's network interfaces, where the server manages its network; otherwise,
    /// <see langword="null"/>.
    /// </summary>
    public string[]? NetworkInterfaceNames { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server has had no configuration from the access manager since it
    /// started: it stays <see langword="true"/> until the first answer, even where applying it then fails.
    /// </summary>
    public bool IsRestarted { get; set; }
}