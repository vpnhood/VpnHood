using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.Access;

public class ServerInfo
{
    [JsonConverter(typeof(VersionConverter))]
    public required Version Version { get; set; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version EnvironmentVersion { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] PrivateIpAddresses { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] PublicIpAddresses { get; set; }

    public required ServerStatus Status { get; set; }
    public string? OsInfo { get; set; }
    public string? OsVersion { get; set; }
    public long? TotalMemory { get; set; }
    public string? MachineName { get; set; }
    public int LogicalCoreCount { get; set; }
    public int FreeUdpPortV4 { get; set; }
    public int FreeUdpPortV6 { get; set; }
    public string[]? NetworkInterfaceNames { get; set; }
    public bool IsRestarted { get; set; }
}