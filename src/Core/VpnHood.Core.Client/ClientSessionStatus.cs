using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Core.Client;

internal class ClientSessionStatus(
    ClientSession session,
    Tunnel tunnel,
    TrafficMeter trafficMeter,
    RequestSender requestSender,
    DomainFilteringService domainFilteringService,
    ProxyManager proxyManager,
    ClientStreamHandler streamHandler,
    ClientPacketHandler packetHandler,
    AccessUsage accessUsage)
    : ISessionStatus
{
    private AccessUsage _accessUsage = accessUsage;
    internal void Update(AccessUsage? value) => _accessUsage = value ?? _accessUsage;

    public ConnectorStat ConnectorStatus => requestSender.ConnectorService.Stat;
    public Traffic Speed => trafficMeter.Speed;
    public Traffic SessionTraffic => trafficMeter.Traffic;
    public Traffic SessionSplitTraffic => proxyManager.Traffic;

    public Traffic CycleTraffic {
        get => field + trafficMeter.Traffic;
    } = accessUsage.CycleTraffic;

    public Traffic TotalTraffic {
        get => field + trafficMeter.Traffic;
    } = accessUsage.TotalTraffic;

    public int SessionPacketChannelCount => session.CreatedPacketChannelCount;
    public int StreamTunnelledCount =>
        streamHandler.Stat.TcpTunnelledCount + domainFilteringService.QuicStat.IncludeCount;
    public int StreamPassthruCount =>
        streamHandler.Stat.TcpPassthruCount + domainFilteringService.QuicStat.ExcludeCount;
    // UDP counts as one while it carries the packets, as it did when it was a channel
    public int ActivePacketChannelCount => tunnel.PacketChannelCount + (session.IsUdpOpen ? 1 : 0);
    public bool IsDropQuic => packetHandler.DropQuic; // This is current, don't use session property
    public bool IsTcpProxy => packetHandler.UseTcpProxyIpV4; // This is current, don't use session property
    public bool CanChangeTcpProxy => // the switch acts on IPv4: IPv6 may go by the proxy anyway
        session.Info is { IsTcpProxySupported: true, IsTcpPacketIpV4Supported: true } && !domainFilteringService.IsEnabled;
    public ChannelProtocol ChannelProtocol => session.ChannelProtocol;
    public int UnstableCount { get; set; }
    public int WaitingCount { get; set; }
    public bool CanExtendByRewardedAd => _accessUsage.CanExtendByRewardedAd;
    public int UserReviewRecommended => _accessUsage.UserReviewRecommended;
    public long SessionMaxTraffic => _accessUsage.MaxTraffic;
    public DateTime? SessionExpirationTime => _accessUsage.ExpirationTime;
    public int? ActiveClientCount => _accessUsage.ActiveClientCount;
    public bool IsSecureDnsDetected => packetHandler.IsSecureDnsDetected;
    public bool IsIpV6SupportedByServer => packetHandler.IsIpV6SupportedByServer;
    public bool IsIpV6SupportedByClient => packetHandler.IsIpV6SupportedByClient;
    public bool IsAdapterStarted => session.IsAdapterStarted;
    public ApiError? Error => session.LastException?.ToApiError();
}