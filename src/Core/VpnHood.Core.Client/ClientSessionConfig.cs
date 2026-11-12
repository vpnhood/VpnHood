using VpnHood.Net.Toolkit.Net;
using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

/// <summary>Represents a session's settings: what the server gave at its start, with the client's own.</summary>
internal class ClientSessionConfig
{
    /// <summary>Gets the options the session's VPN adapter starts with.</summary>
    public required VpnAdapterOptions AdapterOptions { get; init; }

    /// <summary>Gets the session's ID.</summary>
    public required ulong SessionId { get; init; }

    /// <summary>Gets the session's key, which its requests carry.</summary>
    public required ReadOnlyMemory<byte> SessionKey { get; init; }

    /// <summary>Gets the transport's tuning (<see cref="ClientOptions.Transport"/>).</summary>
    public required ClientTransportOptions Transport { get; init; }

    /// <summary>
    /// Gets the most packet channels the session holds at once: the client's most, or the server's where it gives a
    /// smaller one above 0.
    /// </summary>
    public required int MaxPacketChannelCount { get; init; }

    /// <summary>Gets the access's speed limits each way, in megabits a second, or <see langword="null"/>.</summary>
    public required Traffic? MaxSpeedMbps { get; init; }

    /// <summary>
    /// Gets the longest age limit of a packet channel (<see cref="ClientOptions.MaxPacketChannelTimespan"/>).
    /// </summary>
    public required TimeSpan MaxPacketChannelLifespan { get; init; }

    /// <summary>
    /// Gets the shortest age limit of a packet channel (<see cref="ClientOptions.MinPacketChannelTimespan"/>).
    /// </summary>
    public required TimeSpan MinPacketChannelLifespan { get; init; }

    /// <summary>Gets the session's DNS plan.</summary>
    public required DnsConfig DnsConfig { get; init; }

    /// <summary>Gets the server's TCP endpoint.</summary>
    public required IPEndPoint? HostTcpEndPoint { get; init; }

    /// <summary>
    /// Gets the server's endpoint for the session's UDP channel, or <see langword="null"/> where it offers none or the
    /// session's protocol is below <see cref="VpnHood.Core.Tunneling.Channels.UdpDatagram.MinProtocolVersion"/>.
    /// </summary>
    public required IPEndPoint? HostUdpEndPoint { get; init; }

    /// <summary>Gets the server's QUIC endpoint, or <see langword="null"/> where it offers none.</summary>
    public required IPEndPoint? HostQuicEndPoint { get; init; }

    /// <summary>Gets the largest packet the tunnel carries both ways, which the VPN adapter takes as its MTU.</summary>
    public required int Mtu { get; init; }

    /// <summary>Gets a value indicating whether the server carries IPv6.</summary>
    public required bool IsIpV6SupportedByServer { get; init; }

    /// <summary>Gets the ad the session requires.</summary>
    public required AdRequirement AdRequirement { get; init; }
}