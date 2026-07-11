using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Core.Client;

internal class ClientSessionStatus(
    ClientSession session,
    Tunnel tunnel,
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
    public Traffic Speed => tunnel.TrafficMeter.Speed;
    public Traffic SessionTraffic => tunnel.TrafficMeter.Traffic;
    public Traffic SessionSplitTraffic => proxyManager.Traffic;

    public Traffic CycleTraffic {
        get => field + tunnel.TrafficMeter.Traffic;
    } = accessUsage.CycleTraffic;

    public Traffic TotalTraffic {
        get => field + tunnel.TrafficMeter.Traffic;
    } = accessUsage.TotalTraffic;

    public int SessionPacketChannelCount => session.CreatedPacketChannelCount;
    public int StreamTunnelledCount =>
        streamHandler.Stat.TcpTunnelledCount + domainFilteringService.QuicStat.IncludeCount;
    public int StreamPassthruCount =>
        streamHandler.Stat.TcpPassthruCount + domainFilteringService.QuicStat.ExcludeCount;
    public int ActivePacketChannelCount => tunnel.PacketChannelCount;
    public bool IsDropQuic => packetHandler.DropQuic; // This is current, don't use session property
    public bool IsTcpProxy => packetHandler.UseTcpProxy; // This is current, don't use session property
    public bool CanChangeTcpProxy =>
        session.Info is { IsTcpProxySupported: true, IsTcpPacketSupported: true } && !domainFilteringService.IsEnabled;
    public ChannelProtocol ChannelProtocol => session.ChannelProtocol;
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
    public bool IsAdapterStarted => session.IsAdapterStarted;
    public ApiError? Error => session.LastException?.ToApiError();
}