using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client;

public interface ISessionStatus
{
    ClientConnectorStat ConnectorStat { get; }
    Traffic Speed { get; }
    Traffic SessionTraffic { get; }
    Traffic SessionSplitTraffic { get; }
    Traffic CycleTraffic { get; }
    Traffic TotalTraffic { get; }
    int TcpTunnelledCount { get; }
    int TcpPassthruCount { get; }
    int PacketChannelCount { get; }
    bool IsUdpMode { get; }
    bool CanExtendByRewardedAd { get; }
    long SessionMaxTraffic { get; }
    DateTime? SessionExpirationTime { get; }
    int? ActiveClientCount { get; }
    AdRequest? AdRequest { get; }
}