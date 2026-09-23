using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.Sessions;

public class AppSessionInfo
{
    public required AccessInfo? AccessInfo { get; init; }
    public required DnsConfig DnsConfig { get; init; }
    public required bool IsLocalNetworkAllowed { get; set; }
    public required bool IsTrafficSplitByServer { get; init; }
    public required bool IsIpV6SupportedByServer { get; init; }
    public required CurrentServerLocationInfo? ServerLocationInfo { get; init; }
    public required bool IsPremiumSession { get; init; }
    public required SessionSuppressType SuppressedTo { get; init; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version ServerVersion { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init; }

    public required DateTime CreatedTime { get; init; }
    public required IReadOnlyList<ChannelProtocol> ChannelProtocols { get; init; }
    public required bool IsTcpProxySupported { get; init; }
    public required bool IsTcpPacketSupported { get; init; }
}