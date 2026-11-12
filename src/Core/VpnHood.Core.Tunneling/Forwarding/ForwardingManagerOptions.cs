using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Forwarding;

/// <summary>Represents the options of a <see cref="ForwardingManager"/>.</summary>
public class ForwardingManagerOptions
{
    /// <summary>
    /// Gets how long a UDP socket, or a UDP destination, may stay idle before the manager drops it; a DNS socket goes
    /// after <see cref="TransportDefaults.UdpDnsTimeout"/> instead.
    /// </summary>
    public required TimeSpan UdpTimeout { get; init; }

    /// <summary>
    /// Gets how long a ping's destination is remembered without a packet, after which its next packet goes to the
    /// callbacks as a new one.
    /// </summary>
    public required TimeSpan IcmpTimeout { get; init; }

    /// <summary>Gets the most UDP sockets the manager keeps open at once, for packets other than DNS.</summary>
    public required int MaxUdpClientCount { get; init; }

    /// <summary>
    /// Gets the most UDP sockets the manager keeps open at once for DNS, apart from the others. The default is
    /// <see cref="TransportDefaults.MaxUdpDnsClientCount"/>.
    /// </summary>
    public int MaxUdpDnsClientCount { get; init; } = TransportDefaults.MaxUdpDnsClientCount;

    /// <summary>Gets the most pings the manager keeps open at once.</summary>
    public required int MaxPingClientCount { get; init; }

    /// <summary>Gets the number of packets each UDP socket's send queue holds.</summary>
    public required int PacketQueueCapacity { get; init; }

    /// <summary>Gets the UDP sockets' send and receive buffer sizes, or <see langword="null"/> for the system's.</summary>
    public required TransferBufferSize? UdpBufferSize { get; init; }

    /// <summary>Gets a value indicating whether the manager forwards pings; otherwise, it refuses them.</summary>
    public required bool IsPingSupported { get; init; }

    /// <summary>Gets what hears of the destinations its UDP and pings reach, or <see langword="null"/>.</summary>
    public required IPacketForwarderCallbacks? PacketForwarderCallbacks { get; init; }

    /// <summary>Gets the log scope of its lines, or <see langword="null"/>.</summary>
    public required LogScope? LogScope { get; init; }
}