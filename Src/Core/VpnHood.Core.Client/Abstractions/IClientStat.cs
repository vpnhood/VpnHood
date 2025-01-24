using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Client.Abstractions;

public interface IClientStat
{
    AccessInfo? AccessInfo { get; }
    ConnectorStat ConnectorStat { get; }
    Traffic Speed { get; }
    Traffic SessionTraffic { get; }
    Traffic CycleTraffic { get; }
    Traffic TotalTraffic { get; }
    int TcpTunnelledCount { get; }
    int TcpPassthruCount { get; }
    int DatagramChannelCount { get; }
    bool IsUdpMode { get; }
    bool IsUdpChannelSupported { get; }
    bool IsWaitingForAd { get; }
    bool IsDnsServersAccepted { get; }
    ServerLocationInfo? ServerLocationInfo { get; }
    Version ServerVersion { get; }
    bool CanExtendByRewardedAd { get; }
    bool IsPremiumSession { get; }
    long SessionMaxTraffic { get; }
    DateTime? SessionExpirationTime { get; }
    int? ActiveClientCount { get; }
    SessionSuppressType SuppressedTo { get; }
    SessionSuppressType SuppressedBy { get; }
    string? ClientCountry { get; }


    [JsonConverter(typeof(IPAddressConverter))]
    IPAddress ClientPublicIpAddress { get; }
}