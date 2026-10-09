using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A passthrough transport whose first send does not complete at once, as a passthrough send must, until Release
public class TestDelayedPassthroughPacketTransport()
    : PacketTransportBase(new PacketTransportOptions { AutoDisposePackets = true, Blocking = false },
        singleMode: true, passthrough: true)
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _sendCount;

    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        return Interlocked.Increment(ref _sendCount) == 1 ? new ValueTask(_release.Task) : default;
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
