using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Dtos;

public class AccessPoint
{
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpAddress { get; init; } 
    public required AccessPointMode AccessPointMode { get; init; }
    public required bool IsListen { get; init; }
    public required int TcpPort { get; init; }
    public required int UdpPort { get; init; }
}