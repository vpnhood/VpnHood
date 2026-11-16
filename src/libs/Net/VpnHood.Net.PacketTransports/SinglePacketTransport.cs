using VpnHood.Net.Packets;

namespace VpnHood.Net.PacketTransports;

/// <summary>
/// Provides a packet transport that queues sent packets and hands them to the derived class one at a time,
/// through <see cref="SendPacketAsync"/>, on a sending loop of its own.
/// </summary>
/// <param name="options">The queue's capacity, blocking and packet disposal.</param>
/// <exception cref="ArgumentOutOfRangeException">
/// The blocking timeout is negative or too long, or the queue's capacity is not above zero.
/// </exception>
public abstract class SinglePacketTransport(PacketTransportOptions options)
    : PacketTransportBase(options, singleMode: true, passthrough: false)
{
    /// <summary>
    /// Hands the batch's one packet to <see cref="SendPacketAsync"/>.
    /// </summary>
    /// <param name="ipPackets">The batch: empty, or one packet.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The batch holds more than one packet.</exception>
    protected sealed override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        return ipPackets.Count switch {
            0 => default,
            > 1 => throw new ArgumentOutOfRangeException(nameof(ipPackets),
                "ipPackets should not be more than 1 in SinglePacketTransport"),
            _ => SendPacketAsync(ipPackets[0])
        };
    }

    /// <summary>
    /// Sends one packet. A throw drops the packet, so do not throw after handing it on.
    /// </summary>
    /// <param name="ipPacket">The packet.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract ValueTask SendPacketAsync(IpPacket ipPacket);
}
