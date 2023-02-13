using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.Configurations;

public class ServerInfo
{
    [JsonConstructor]
    public ServerInfo(Version version,
        Version environmentVersion,
        IPAddress[] privateIpAddresses,
        IPAddress[] publicIpAddresses,
        ServerStatus status)
    {
        Version = version;
        EnvironmentVersion = environmentVersion;
        PrivateIpAddresses = privateIpAddresses;
        PublicIpAddresses = publicIpAddresses;
        Status = status;
    }

    [JsonConverter(typeof(VersionConverter))]
    public Version Version { get; set; }

    [JsonConverter(typeof(VersionConverter))]
    public Version EnvironmentVersion { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[] PrivateIpAddresses { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[] PublicIpAddresses { get; set; }
    public ServerStatus Status { get; set; }
    public string? OsInfo { get; set; }
    public string? OsVersion { get; set; }
    public long? TotalMemory { get; set; }
    public string? MachineName { get; set; }
    public string? LastError { get; set; }
    public int LogicalCoreCount { get; set; }
}