using VpnHood.Core.Client.VpnServices.Host;

namespace VpnHood.Test.Device;

// A hold on TestVpnTunnelLock's tunnel, which an ask ends once its holder returns.
public class TestVpnTunnelHold(TestVpnTunnelLock tunnelLock, IVpnTunnelHolder holder) : IDisposable
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _isAsked;

    public Task Released => _released.Task;

    internal void Ask()
    {
        if (Interlocked.Exchange(ref _isAsked, 1) == 1)
            return;

        holder.OnTunnelRevoked();
        Dispose();
    }

    public void Dispose()
    {
        tunnelLock.WaitForHoldDispose();
        tunnelLock.Release(this);
        _released.TrySetResult();
    }
}
