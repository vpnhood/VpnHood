using VpnHood.Net.Packets;

namespace VpnHood.Net.PacketTransports;

public abstract class PassthroughPacketTransport()
    : PacketTransportBase(new PacketTransportOptions {
        AutoDisposePackets = false,
        Blocking = false
    }, passthrough: true, singleMode: true)
{
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

    // Hands the packet on, or throws before it does: once handed on, it is no longer this transport's
    protected abstract void SendPacket(IpPacket ipPacket);
}