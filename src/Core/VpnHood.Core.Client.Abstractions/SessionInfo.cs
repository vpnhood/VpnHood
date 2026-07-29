using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

public class SessionInfo
{
    public required string SessionId { get; init; }
    public required AccessInfo? AccessInfo { get; init; }
    public required DateTime CreatedTime { get; set; }
    public required bool IsUdpChannelSupported { get; init; }
    public required bool IsQuicChannelSupported { get; init; }
    public required bool IsTcpPacketSupported { get; init; }
    public required bool IsTcpProxySupported { get; init; }
    public required bool IsLocalNetworkAllowed { get; set; }
    public required ServerLocationInfo? ServerLocationInfo { get; init; }
    public required bool IsPremiumSession { get; init; }
    public required SessionSuppressType SuppressedTo { get; init; }
    public required AdRequirement AdRequirement { get; init; }
    public required IReadOnlyList<ChannelProtocol> ChannelProtocols { get; init; }
    public required string? AccessKey { get; set; } // allow set to let clear
    public required string? ClientCountry { get; init; }
    public required DnsConfig DnsConfig { get; init; }

    // True when the server's own configuration leaves PUBLIC destinations outside the tunnel — either of
    // its declarations (app or adapter ranges) covers less than the public internet. Carve-outs of
    // local/special ranges (the usual LAN skip) do not count: they cannot expose the public IP, and
    // counting them would light the split indicator on nearly every server. A family the server cannot
    // carry at all is not counted either — IPv6 absence is reported via IsIpV6SupportedByServer and judged
    // by UnsupportedIpV6Mode, not as server splitting.
    public required bool IsTrafficSplitByServer { get; init; }

    // False for a v4-only server: the client then blocks or bypasses IPv6 per UnsupportedIpV6Mode.
    public required bool IsIpV6SupportedByServer { get; init; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version ServerVersion { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init; }
}