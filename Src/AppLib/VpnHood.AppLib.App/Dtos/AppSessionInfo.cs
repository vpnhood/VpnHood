﻿using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.Dtos;

public class AppSessionInfo
{
    public required AccessInfo? AccessInfo { get; init; }
    public required bool IsDnsServersAccepted { get; init; }
    public required bool IsLocalNetworkAllowed { get; set; }
    public required ServerLocationInfo? ServerLocationInfo { get; init; }
    public required bool IsPremiumSession { get; init; }
    public required SessionSuppressType SuppressedTo { get; init; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] DnsServers { get; init; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version ServerVersion { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required ChannelProtocol[] ChannelProtocols { get; init; }
    public required bool IsTcpProxySupported { get; init; }
    public required bool IsTcpPacketSupported { get; init; }
    public bool CanChangeTcpProxy => IsTcpProxySupported && IsTcpPacketSupported;
}