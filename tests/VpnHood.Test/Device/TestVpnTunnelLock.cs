using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Test.Device;

// The machine's one tunnel in this process, for tests: a take asks the holder to let go, once, and the
// hold ends as soon as the holder returns, as the desktops' locks do across processes.
public class TestVpnTunnelLock : IVpnTunnelLock
{
    private readonly Lock _lock = new();
    private TestVpnTunnelHold? _hold;

    public bool IsHeld {
        get {
            lock (_lock) return _hold != null;
        }
    }

    public async Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken)
    {
        while (true) {
            TestVpnTunnelHold current;
            lock (_lock) {
                if (_hold == null)
                    return _hold = new TestVpnTunnelHold(this, holder);

                current = _hold;
            }

            current.Ask();
            await current.Released.WaitAsync(cancellationToken).Vhc();
        }
    }

    internal void Release(TestVpnTunnelHold hold)
    {
        lock (_lock) {
            if (_hold == hold)
                _hold = null;
        }
    }
}
