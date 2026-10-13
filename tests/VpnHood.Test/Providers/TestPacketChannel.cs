using VpnHood.Core.Tunneling.Channels;
using VpnHood.Net.Packets;

namespace VpnHood.Test.Providers;

// A packet channel with no connection behind it: it counts the packets and bytes sent through it and disposes
// the packets
public class TestPacketChannel(TimeSpan? lifespan = null) : PacketChannel(new PacketChannelOptions {
    AutoDisposePackets = true,
    Blocking = false,
    Lifespan = lifespan,
    ChannelId = Guid.NewGuid().ToString()
})
{
    private int _sentPackets;
    private long _sentBytes;

    public int SentPackets => Volatile.Read(ref _sentPackets);
    public long SentBytes => Interlocked.Read(ref _sentBytes);
    public override int OverheadLength => 0;

    // a packet arriving from the other side
    public void Receive(IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    // nothing to read: it waits until the channel is disposed
    protected override Task StartReadTask()
    {
        return Task.Delay(Timeout.Infinite, CancellationToken);
    }

    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        foreach (var ipPacket in ipPackets)
            Interlocked.Add(ref _sentBytes, ipPacket.PacketLength);

        Interlocked.Add(ref _sentPackets, ipPackets.Count);
        return default;
    }
}
