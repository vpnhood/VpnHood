using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

// A null adapter the test breaks: from then on each read fails, until the adapter stops itself on the
// errors and tells its owner (IVpnAdapter.Failed), as one whose interface went does.
public sealed class BreakableVpnAdapter() : NullVpnAdapter(blocking: false)
{
    private readonly ManualResetEventSlim _brokenEvent = new(false);

    public void Break()
    {
        _brokenEvent.Set();
    }

    protected override void WaitForTunRead()
    {
        _brokenEvent.Wait();
    }

    protected override int ReadPacket(byte[] buffer)
    {
        return _brokenEvent.IsSet ? throw new IOException("The test broke the adapter.") : 0;
    }

    protected override void DisposeManaged()
    {
        _brokenEvent.Set(); // the reader's wait ends; its run is over by now
        base.DisposeManaged();
        _brokenEvent.Dispose();
    }
}
