﻿using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client;

public interface ISessionStatus
{
    ProxyManagerStatus ProxyManagerStatus { get; }
    ClientConnectorStatus ConnectorStatus { get; }
    Traffic Speed { get; }
    Traffic SessionTraffic { get; }
    Traffic SessionSplitTraffic { get; }
    Traffic CycleTraffic { get; }
    Traffic TotalTraffic { get; }
    int TcpTunnelledCount { get; }
    int TcpPassthruCount { get; }
    int UnstableCount { get; }
    int WaitingCount { get; }
    int SessionPacketChannelCount { get; }
    int ActivePacketChannelCount { get; }
    ChannelProtocol ChannelProtocol { get; }
    bool CanExtendByRewardedAd { get; }
    int UserReviewRecommended { get; }
    long SessionMaxTraffic { get; }
    DateTime? SessionExpirationTime { get; }
    int? ActiveClientCount { get; }
    bool IsDnsOverTlsDetected { get; }
}