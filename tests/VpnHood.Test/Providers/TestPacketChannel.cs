using VpnHood.Core.Tunneling.Channels;
using VpnHood.Net.Packets;

namespace VpnHood.Test.Providers;

// A packet channel with no connection behind it: it counts the packets sent through it and disposes them
public class TestPacketChannel() : PacketChannel(new PacketChannelOptions {
    AutoDisposePackets = true,
    Blocking = false,
    Lifespan = null,
    ChannelId = Guid.NewGuid().ToString()
})
{
    private int _sentPackets;

    public int SentPackets => Volatile.Read(ref _sentPackets);
    public override int OverheadLength => 0;

    // nothing to read: it waits until the channel is disposed
    protected override Task StartReadTask()
    {
        return Task.Delay(Timeout.Infinite, CancellationToken);
    }

    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        Interlocked.Add(ref _sentPackets, ipPackets.Count);
        return default;
    }
}
