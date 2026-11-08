using VpnHood.Net.Packets;

namespace VpnHood.Net.PacketTransports;

/// <summary>
/// Provides a packet transport with no queue: each packet is handed on at once, on the sender's thread, through
/// <see cref="SendPacket"/>.
/// </summary>
/// <remarks>
/// It never disposes a packet it hands on; one <see cref="SendPacket"/> fails on is disposed.
/// </remarks>
public abstract class PassthroughPacketTransport()
    : PacketTransportBase(new PacketTransportOptions {
        AutoDisposePackets = false,
        Blocking = false
    }, passthrough: true, singleMode: true)
{
    /// <summary>
    /// Hands the batch's one packet to <see cref="SendPacket"/>, and disposes it when that throws.
    /// </summary>
    /// <param name="ipPackets">The batch: empty, or one packet.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The batch holds more than one packet.</exception>
    protected sealed override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        switch (ipPackets.Count) {
            case 0:
                return default;
            case > 1:
                throw new ArgumentOutOfRangeException(nameof(ipPackets),
                    "ipPackets should not be more than 1 in SinglePacketTransport");
            default:
                // a packet SendPacket could not hand on went to no one, so it is disposed here; one a transport it
                // was handed to refused is disposed already, and a second Dispose does nothing
                try {
                    SendPacket(ipPackets[0]);
                }
                catch {
                    ipPackets[0].Dispose();
                    throw;
                }

                return default;
        }
    }

    /// <summary>
    /// Hands one packet on, at once. Once handed on, the packet is no longer this transport's; throw only before.
    /// </summary>
    /// <param name="ipPacket">The packet.</param>
    protected abstract void SendPacket(IpPacket ipPacket);
}
