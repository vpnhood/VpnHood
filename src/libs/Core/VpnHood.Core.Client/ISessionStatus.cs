using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.Core.Client;

/// <summary>
/// Represents a session's state now: its traffic, its channels, and what its access allows.
/// </summary>
public interface ISessionStatus
{
    /// <summary>Gets the statistics of the client's connections to the server.</summary>
    ConnectorStat ConnectorStatus { get; }

    /// <summary>Gets the tunnel's speed now, sent and received, in bytes per second.</summary>
    Traffic Speed { get; }

    /// <summary>Gets the traffic the tunnel carried in this session.</summary>
    Traffic SessionTraffic { get; }

    /// <summary>
    /// Gets the traffic the client sent around the tunnel in this session, by the splits and while it waited for an ad.
    /// </summary>
    Traffic SessionSplitTraffic { get; }

    /// <summary>
    /// Gets the access's traffic in its current cycle: what it was when the session began, and this session's since.
    /// </summary>
    Traffic CycleTraffic { get; }

    /// <summary>
    /// Gets the access's traffic in all: what it was when the session began, and this session's since; that part is zero
    /// where the access manager does not report it.
    /// </summary>
    Traffic TotalTraffic { get; }

    /// <summary>
    /// Gets how many TCP connections the client forwarded through the tunnel in this session, and QUIC flows whose
    /// domain name the client read and the domain filter included or left to the default.
    /// </summary>
    int StreamTunnelledCount { get; }

    /// <summary>
    /// Gets how many TCP connections the client sent around the tunnel in this session, and QUIC flows whose domain name
    /// the client read and the domain filter excluded.
    /// </summary>
    int StreamPassthruCount { get; }

    /// <summary>
    /// Gets how many times the session went from <see cref="VpnHood.Core.Client.Abstractions.ClientState.Connected"/> to
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientState.Unstable"/>.
    /// </summary>
    int UnstableCount { get; }

    /// <summary>
    /// Gets how many times a request failed past the unstable timeout and paused the session
    /// (<see cref="VpnHood.Core.Client.Abstractions.ClientState.Waiting"/>), a pause while it already waits counted too.
    /// </summary>
    int WaitingCount { get; }

    internal int SessionPacketChannelCount { get; }

    /// <summary>
    /// Gets the packet channels the tunnel holds now, a closed one not cleaned up yet among them; the UDP channel counts
    /// as one while it is open.
    /// </summary>
    int ActivePacketChannelCount { get; }

    /// <summary>Gets the protocol of the session's packet channels now.</summary>
    ChannelProtocol ChannelProtocol { get; }

    /// <summary>
    /// Gets a value indicating whether the client drops the QUIC bound for the tunnel now, by the setting and the server.
    /// </summary>
    bool IsDropQuic { get; }

    /// <summary>Gets a value indicating whether the client forwards IPv4 TCP now, by the setting and the server.</summary>
    bool IsTcpForwarding { get; }

    /// <summary>
    /// Gets a value indicating whether the person can switch TCP forwarding: the server can both forward IPv4 TCP and
    /// carry it as packets, and no domain filter needs forwarding.
    /// </summary>
    bool CanChangeTcpForwarding { get; }

    /// <summary>Gets a value indicating whether a rewarded ad can extend the session now.</summary>
    bool CanExtendByRewardedAd { get; }

    /// <summary>
    /// Gets whether the server recommends that the app asks its user for a review: above 0 if so, 0 if not.
    /// </summary>
    int UserReviewRecommended { get; }

    /// <summary>
    /// Gets the access's traffic quota, in bytes, as the server last reported it
    /// (<see cref="AccessUsage.MaxTraffic"/>); 0 for none.
    /// </summary>
    long SessionMaxTraffic { get; }

    /// <summary>
    /// Gets when the session expires, in UTC, or <see langword="null"/> where the access manager reports no time.
    /// </summary>
    DateTime? SessionExpirationTime { get; }

    /// <summary>Gets the number of clients using the access now, or <see langword="null"/> where not told.</summary>
    int? ActiveClientCount { get; }

    /// <summary>
    /// Gets a value indicating whether the device sent traffic to port 853 in this session, the port of DNS over TLS and
    /// over QUIC; the payload is not read.
    /// </summary>
    bool IsSecureDnsDetected { get; }

    internal bool IsIpV6SupportedByServer { get; }
    internal bool IsIpV6SupportedByClient { get; }

    /// <summary>Gets the error that ended the session, or <see langword="null"/>.</summary>
    ApiError? Error { get; }

    /// <summary>Gets a value indicating whether the session's VPN adapter has started.</summary>
    bool IsAdapterStarted { get; }
}