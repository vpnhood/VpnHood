using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A transport that does not block, whose batches are held at a barrier until Release: a queue whose
// connection stopped moving
public class TestHeldPacketTransport(int queueCapacity)
    : PacketTransport(new PacketTransportOptions {
        QueueCapacity = queueCapacity,
        AutoDisposePackets = true,
        Blocking = false
    })
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<int> _sentPorts = [];
    public volatile bool IsBatchHeld;

    // the UDP source ports of the packets sent, in order
    public IReadOnlyList<int> SentPorts {
        get {
            lock (_sentPorts)
                return _sentPorts.ToArray();
        }
    }

    protected override async ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        IsBatchHeld = !_release.Task.IsCompleted;
        await _release.Task;
        IsBatchHeld = false;
        lock (_sentPorts)
            foreach (var ipPacket in ipPackets)
                _sentPorts.Add(ipPacket.ExtractUdp().SourcePort);
    }

    public void Release()
    {
        _release.TrySetResult();
    }

    protected override void DisposeManaged()
    {
        _release.TrySetResult();
        base.DisposeManaged();
    }
}
