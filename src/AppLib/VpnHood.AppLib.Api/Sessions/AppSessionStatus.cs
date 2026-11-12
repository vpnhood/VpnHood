using VpnHood.AppLib.Api.Proxies;

namespace VpnHood.AppLib.Api.Sessions;

/// <summary>
/// Represents a session's state as the VPN service reports it to the app: its traffic, its channels, and what its
/// access allows.
/// </summary>
public class AppSessionStatus
{
    /// <summary>Gets the statistics of the connections the client opens to the server.</summary>
    public required AppConnectorStat ConnectorStat { get; init; }

    /// <summary>Gets the tunnel's speed now, sent and received, in bytes per second.</summary>
    public required Traffic Speed { get; init; }

    /// <summary>Gets the traffic the tunnel carried in this session.</summary>
    public required Traffic SessionTraffic { get; init; }

    /// <summary>
    /// Gets the traffic the client sent around the tunnel in this session, by the splits and while it waited for an ad.
    /// </summary>
    public required Traffic SessionSplitTraffic { get; init; }

    /// <summary>
    /// Gets the access's traffic in its current cycle: what it was when the session began, and this session's since.
    /// </summary>
    public required Traffic CycleTraffic { get; init; }

    /// <summary>
    /// Gets the access's traffic in all: what it was when the session began, and this session's since; that part is zero
    /// where the access manager does not report it.
    /// </summary>
    public required Traffic TotalTraffic { get; init; }

    /// <summary>
    /// Gets how many TCP connections the client forwarded through the tunnel in this session, and QUIC flows whose
    /// domain name the client read and the domain filter included or left to the default.
    /// </summary>
    public required int StreamTunnelledCount { get; init; }

    /// <summary>
    /// Gets how many TCP connections the client sent around the tunnel in this session, and QUIC flows whose domain name
    /// the client read and the domain filter excluded.
    /// </summary>
    public required int StreamPassthruCount { get; init; }

    /// <summary>
    /// Gets the packet channels the tunnel holds now, a closed one not cleaned up yet among them; the UDP channel counts
    /// as one while it is open.
    /// </summary>
    public required int PacketChannelCount { get; init; }

    /// <summary>
    /// Gets how many times the session went from <see cref="VpnHood.AppLib.Api.App.AppConnectionState.Connected"/> to
    /// <see cref="VpnHood.AppLib.Api.App.AppConnectionState.Unstable"/>.
    /// </summary>
    public required int UnstableCount { get; set; }

    /// <summary>
    /// Gets how many times a request failed past the unstable timeout and paused the session
    /// (<see cref="VpnHood.AppLib.Api.App.AppConnectionState.Waiting"/>), a pause while it already waits counted too.
    /// </summary>
    public required int WaitingCount { get; set; }

    /// <summary>Gets a value indicating whether a rewarded ad can extend the session now.</summary>
    public required bool CanExtendByRewardedAd { get; init; }

    /// <summary>
    /// Gets the access's traffic quota, in bytes, as the server last reported it; 0 for none.
    /// </summary>
    public required long SessionMaxTraffic { get; init; }

    /// <summary>
    /// Gets when the session expires, in UTC, or <see langword="null"/> where the access manager reports no time.
    /// </summary>
    public required DateTime? SessionExpirationTime { get; init; }

    /// <summary>Gets the number of clients using the access now, or <see langword="null"/> where not told.</summary>
    public required int? ActiveClientCount { get; init; }

    /// <summary>Gets a value indicating whether the client forwards IPv4 TCP now, by the setting and the server.</summary>
    public required bool IsTcpForwarding { get; init; }

    /// <summary>
    /// Gets a value indicating whether the person can switch TCP forwarding: the server can both forward IPv4 TCP and
    /// carry it as packets, and no domain filter needs forwarding.
    /// </summary>
    public required bool CanChangeTcpForwarding { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the client drops the QUIC bound for the tunnel now, by the setting and
    /// the server.
    /// </summary>
    public required bool IsDropQuic { get; init; }

    /// <summary>Gets the protocol of the session's packet channels now.</summary>
    public required ChannelProtocol ChannelProtocol { get; init; }
}