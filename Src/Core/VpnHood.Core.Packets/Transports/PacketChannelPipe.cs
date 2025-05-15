namespace VpnHood.Core.Packets.Transports;

public abstract class PacketChannelPipe(bool autoDisposePackets)
    : PacketChannelSingle(new PacketChannelOptions {
        AutoDisposeFailedPackets = autoDisposePackets,
        AutoDisposeSentPackets = false, // always false for pipe
        Blocking = false
    })
{
    protected sealed override ValueTask SendPacketAsync(IpPacket ipPacket)
    {
        SendPacket(ipPacket);
        return default;
    }

    protected abstract void SendPacket(IpPacket ipPacket);

}
