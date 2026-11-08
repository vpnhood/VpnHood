using VpnHood.Net.Packets;

namespace VpnHood.Net.PacketTransports;

/// <summary>
/// Represents a two-way channel of IP packets: packets sent to it are queued and sent on, and packets it receives
/// are raised through <see cref="PacketReceived"/>.
/// </summary>
/// <remarks>
/// A packet handed to the transport, or raised by it, has one owner at a time: whoever holds it disposes it, or
/// hands it on.
/// </remarks>
public interface IPacketTransport : IDisposable
{
    /// <summary>
    /// Occurs when the transport receives a packet. The handler takes the packet: it delivers or disposes it, or
    /// throws to give it back to the transport, which disposes it. Attach one handler only: a second could see a
    /// packet the first one disposed or handed on.
    /// </summary>
    event EventHandler<IpPacket>? PacketReceived;

    /// <summary>
    /// Gets a value indicating whether the transport has packets queued or a batch in flight.
    /// </summary>
    bool IsSending { get; }

    /// <summary>
    /// Queues a packet to send. The transport takes the packet whatever the outcome: it is never the caller's
    /// again.
    /// </summary>
    /// <param name="ipPacket">The packet.</param>
    /// <returns>
    /// <see langword="true"/> if the packet was queued, or sent; <see langword="false"/> if it was refused or
    /// dropped, and disposed.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The transport is disposed; the packet is disposed too.</exception>
    /// <exception cref="InvalidOperationException">A passthrough transport's send did not complete at once.</exception>
    bool SendPacketQueued(IpPacket ipPacket);

    /// <summary>
    /// Queues a packet to send, waiting without a thread for room when the queue is full and the transport
    /// blocks. The transport takes the packet whatever the outcome.
    /// </summary>
    /// <param name="ipPacket">The packet.</param>
    /// <returns>A task that represents the asynchronous operation. It completes when the packet is queued or dropped.</returns>
    /// <exception cref="ObjectDisposedException">The transport is disposed; the packet is disposed too.</exception>
    /// <exception cref="InvalidOperationException">A passthrough transport's send did not complete at once.</exception>
    /// <exception cref="System.Threading.Channels.ChannelClosedException">
    /// The queue closed during the send, as the transport is being disposed; the packet is disposed too.
    /// </exception>
    ValueTask SendPacketQueuedAsync(IpPacket ipPacket);

    /// <summary>
    /// Gets the transport's counters: packets and bytes sent and received, drops, and the times of the last
    /// activity.
    /// </summary>
    ReadOnlyPacketTransportStat PacketStat { get; }
}
