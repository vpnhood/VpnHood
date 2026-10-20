using System.IO.Pipelines;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Net.Packets;

namespace VpnHood.Test.Providers;

// A stream packet channel whose stream carries nothing: it counts the packets and bytes sent through it and
// disposes the packets
public class TestPacketChannel(TimeSpan? lifespan = null) : StreamPacketChannel(new StreamPacketChannelOptions {
    AutoDisposePackets = true,
    Blocking = false,
    Lifespan = lifespan,
    ChannelId = Guid.NewGuid().ToString(),
    RequestTime = DateTime.UtcNow,

    // nothing is written to the pipe, so the channel's reads wait until it is disposed
    StreamConnection = new TestStreamConnection(new Pipe().Reader.AsStream())
})
{
    private int _sentPackets;
    private long _sentBytes;

    public int SentPackets => Volatile.Read(ref _sentPackets);
    public long SentBytes => Interlocked.Read(ref _sentBytes);

    // a packet arriving from the other side
    public void Receive(IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    // counted, not written to the stream
    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        foreach (var ipPacket in ipPackets)
            Interlocked.Add(ref _sentBytes, ipPacket.PacketLength);

        Interlocked.Add(ref _sentPackets, ipPackets.Count);
        return default;
    }
}
