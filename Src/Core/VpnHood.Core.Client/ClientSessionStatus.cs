using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Core.Client;

internal class ClientSessionStatus(
    Tunnel tunnel,
    ConnectorService connectorService, 
    ProxyManager proxyManager,
    ClientStreamHandler streamHandler,
    ClientPacketHandler packetHandler,
    AccessUsage accessUsage) 
    : ISessionStatus
{
    private AccessUsage _accessUsage = accessUsage;
    internal void Update(AccessUsage? value) => _accessUsage = value ?? _accessUsage;

    public ClientConnectorStatus ConnectorStatus => connectorService.Status;
    public Traffic Speed => tunnel.Speed;
    public Traffic SessionTraffic => tunnel.Traffic;
    public Traffic SessionSplitTraffic => proxyManager.Traffic;

    public Traffic CycleTraffic {
        get => field + tunnel.Traffic;
    } = accessUsage.CycleTraffic;

    public Traffic TotalTraffic {
        get => field + tunnel.Traffic;
    } = accessUsage.TotalTraffic;

    public int SessionPacketChannelCount => client._sessionPacketChannelCount;
    public int TcpTunnelledCount => streamHandler.Stat.TcpTunnelledCount;
    public int TcpPassthruCount => streamHandler.Stat.TcpPassthruCount;
    public int ActivePacketChannelCount => tunnel.PacketChannelCount;
    public bool IsDropQuic => packetHandler.DropQuic;
    public bool IsTcpProxy => packetHandler.UseTcpProxy;
    public ChannelProtocol ChannelProtocol => client.ChannelProtocol;
    public int UnstableCount { get; set; }
    public int WaitingCount { get; set; }
    public bool CanExtendByRewardedAd => _accessUsage.CanExtendByRewardedAd;
    public int UserReviewRecommended => _accessUsage.UserReviewRecommended;
    public long SessionMaxTraffic => _accessUsage.MaxTraffic;
    public DateTime? SessionExpirationTime => _accessUsage.ExpirationTime;
    public int? ActiveClientCount => _accessUsage.ActiveClientCount;
    public bool IsDnsOverTlsDetected => packetHandler.IsDnsOverTlsDetected;
    public bool IsIpV6SupportedByServer => packetHandler.IsIpV6SupportedByServer;
    public bool IsIpV6SupportedByClient => packetHandler.IsIpV6SupportedByClient;
}