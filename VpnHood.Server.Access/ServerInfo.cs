using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.Access;

public class ServerInfo
{
    [JsonConverter(typeof(VersionConverter))]
    public required Version Version { get; init; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version EnvironmentVersion { get; init; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] PrivateIpAddresses { get; init; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] PublicIpAddresses { get; init; }

    public required ServerStatus Status { get; init; }
    public string? OsInfo { get; init; }
    public string? OsVersion { get; init; }
    public long? TotalMemory { get; init; }
    public long? TotalSwapMemory { get; init; }
    public string? MachineName { get; init; }
    public int LogicalCoreCount { get; init; }
    public int FreeUdpPortV4 { get; init; }
    public int FreeUdpPortV6 { get; init; }
    public string[]? NetworkInterfaceNames { get; init; }
}