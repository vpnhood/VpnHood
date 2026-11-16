using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Client;

/// <summary>
/// Represents a client's settings, from the options it was made with (<see cref="ClientOptions"/>).
/// </summary>
public class VpnHoodClientConfig
{
    // 10: Support RedirectHostServerTokens (7.5.770)
    // 11: New UDP AesGcm encryption 
    // 12: fix server WebSocketNoHandshake 2026/02
    // 13: server QUIC 2026/05; no client sent it
    // 14: UDP with the server-reply bit and the freshness mark; older peers get no UDP 2026/10
    // 15: prove the farm key in every HTTP request head; upgrade servers before these clients.
    internal static int MaxProtocolVersion => 15;

    /// <summary>Gets the oldest protocol version the client takes a session on: 8.</summary>
    internal static int MinProtocolVersion => 8;

    internal VpnHoodClientConfig()
    {
    }

    /// <summary>Gets the client's version, which the servers get (<see cref="ClientOptions.Version"/>).</summary>
    public required Version Version { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client disposes its VPN adapter with itself
    /// (<see cref="ClientOptions.AutoDisposeVpnAdapter"/>).
    /// </summary>
    public required bool AutoDisposeVpnAdapter { get; init; }

    /// <summary>Gets the transport's tuning (<see cref="ClientOptions.Transport"/>).</summary>
    public required ClientTransportOptions Transport { get; init; }

    /// <summary>
    /// Gets the shortest age limit of a packet channel (<see cref="ClientOptions.MinPacketChannelTimespan"/>).
    /// </summary>
    public required TimeSpan MinPacketChannelLifespan { get; init; }

    /// <summary>
    /// Gets the longest age limit of a packet channel (<see cref="ClientOptions.MaxPacketChannelTimespan"/>).
    /// </summary>
    public required TimeSpan MaxPacketChannelLifespan { get; init; }

    /// <summary>
    /// Gets a value indicating whether the person lets their use be reported anonymously
    /// (<see cref="ClientOptions.AllowAnonymousTracker"/>).
    /// </summary>
    public required bool AllowAnonymousTracker { get; init; }

    /// <summary>Gets the plan to connect with (<see cref="ClientOptions.PlanId"/>).</summary>
    public required ConnectPlanId PlanId { get; init; }

    /// <summary>
    /// Gets the premium code to connect with, or <see langword="null"/> (<see cref="ClientOptions.AccessCode"/>).
    /// </summary>
    public required string? AccessCode { get; init; }

    /// <summary>
    /// Gets the only apps to let into the tunnel, or <see langword="null"/> for every app
    /// (<see cref="ClientOptions.IncludeApps"/>).
    /// </summary>
    public required string[]? IncludeApps { get; init; }

    /// <summary>
    /// Gets the apps to keep out of the tunnel, or <see langword="null"/> (<see cref="ClientOptions.ExcludeApps"/>).
    /// </summary>
    public required string[]? ExcludeApps { get; init; }

    /// <summary>
    /// Gets the addresses the device's tunnel captures, before the server's ranges and the client's other settings shape
    /// them (<see cref="ClientOptions.IncludeIpRangesByDevice"/>).
    /// </summary>
    public required IReadOnlyList<IpRange> IncludeIpRangesByDevice { get; init; }

    /// <summary>
    /// Gets the DNS servers the person chose, or <see langword="null"/> for the automatic choice
    /// (<see cref="ClientOptions.DnsServers"/>).
    /// </summary>
    public required IReadOnlyList<IPAddress>? DnsServers { get; init; }

    /// <summary>Gets the client's ID (<see cref="ClientOptions.ClientId"/>).</summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the local networks go through the VPN: the reverse of
    /// <see cref="ClientOptions.SplitLocalNetwork"/>.
    /// </summary>
    public required bool IncludeLocalNetwork { get; init; }

    /// <summary>Gets where DNS may go (<see cref="ClientOptions.SplitDnsMode"/>).</summary>
    public required SplitDnsMode SplitDnsMode { get; init; }

    /// <summary>
    /// Gets what happens to a destination the server does not route, at the connect
    /// (<see cref="ClientOptions.UnroutedIpMode"/>).
    /// </summary>
    public required SplitUnsupportedIpMode UnroutedIpMode { get; init; }

    /// <summary>
    /// Gets what happens to IPv6 where the server carries none, at the connect
    /// (<see cref="ClientOptions.UnsupportedIpV6Mode"/>).
    /// </summary>
    public required SplitUnsupportedIpMode UnsupportedIpV6Mode { get; init; }

    /// <summary>
    /// Gets the name the VPN adapter gives its session, or <see langword="null"/> (<see cref="ClientOptions.SessionName"/>).
    /// </summary>
    public required string? SessionName { get; init; }

    /// <summary>Gets the user agent the servers get (<see cref="ClientOptions.UserAgent"/>).</summary>
    public required string UserAgent { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client keeps its links to the server for the next request
    /// (<see cref="ClientOptions.AllowChannelReuse"/>).
    /// </summary>
    public required bool AllowStreamReuse { get; init; }

    /// <summary>
    /// Gets the user's review of the app to send with the connection, or <see langword="null"/>
    /// (<see cref="ClientOptions.UserReview"/>).
    /// </summary>
    public required UserReview? UserReview { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client's session requests after the hello go on WebSocket streams: unless the
    /// debug data has <c>/disable-WebSocket</c>.
    /// </summary>
    public required bool UseWebSocket { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client prefers to forward TCP connections, at the connect
    /// (<see cref="ClientOptions.UseTcpForwarding"/>).
    /// </summary>
    public required bool UseTcpForwarding { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client was asked to drop the UDP bound for the tunnel, at the connect
    /// (<see cref="ClientOptions.DropUdp"/>); the session works out what is in effect.
    /// </summary>
    public required bool DropUdp { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client was asked to drop the QUIC bound for the tunnel, at the connect
    /// (<see cref="ClientOptions.DropQuic"/>); the session works out what is in effect.
    /// </summary>
    public required bool DropQuic { get; init; }
}
