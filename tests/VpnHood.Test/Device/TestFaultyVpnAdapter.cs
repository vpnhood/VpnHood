using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

// A null adapter whose start and reads fail on demand, for the adapter's own recovery
public class TestFaultyVpnAdapter(bool autoRestart)
    : NullVpnAdapter(new VpnAdapterSettings {
        AdapterName = "VpnHoodFaultyAdapter",
        Blocking = false,
        AutoDisposePackets = true,
        AutoRestart = autoRestart
    })
{
    public volatile bool FailStart;
    public volatile bool FailRead;
    public int StartCount; // starts tried
    public int OpenCount; // starts completed: IsStarted is true from a start's first step on

    protected override TimeSpan AutoRestartDelay => TimeSpan.FromMilliseconds(200);

    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref StartCount);
        return FailStart
            ? throw new IOException("Test: the adapter could not be added.")
            : base.AdapterAdd(cancellationToken);
    }

    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref OpenCount);
        return base.AdapterOpen(cancellationToken);
    }

    protected override bool ReadPacket(byte[] buffer)
    {
        return FailRead
            ? throw new IOException("Test: the adapter could not be read.")
            : base.ReadPacket(buffer);
    }

    // poll instead of blocking, so a read failure set later is seen
    protected override void WaitForTunRead()
    {
        Thread.Sleep(20);
    }
}
