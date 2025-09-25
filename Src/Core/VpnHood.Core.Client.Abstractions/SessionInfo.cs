﻿using System.Net;
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
    public required bool IsTcpPacketSupported { get; init; }
    public required bool IsTcpProxySupported { get; init; }
    public required bool IsDnsServersAccepted { get; init; }
    public required bool IsLocalNetworkAllowed { get; set; }
    public required ServerLocationInfo? ServerLocationInfo { get; init; }
    public required bool IsPremiumSession { get; init; }
    public required SessionSuppressType SuppressedTo { get; init; }
    public required AdRequirement AdRequirement { get; init; }
    public required ChannelProtocol[] ChannelProtocols { get; init; }
    public required string? AccessKey { get; set; } // allow set to let clear
    public required string? ClientCountry { get; init; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] DnsServers { get; init; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version ServerVersion { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init; }
}
