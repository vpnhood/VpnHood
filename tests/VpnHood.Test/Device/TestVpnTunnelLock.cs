using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Test.Device;

// The machine's one tunnel in this process, for tests: a take asks the holder to let go, once, and the
// hold ends as soon as the holder returns, as the desktops' locks do across processes.
public class TestVpnTunnelLock : IVpnTunnelLock
{
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource _takeWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _holdDisposeWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _takeAcquiredWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TestVpnTunnelHold? _hold;
    private int _takenCount;

    public bool IsHeld {
        get {
            lock (_lock) return _hold != null;
        }
    }

    // the takes that got the tunnel, where a test looks that one did
    public int TakenCount {
        get {
            lock (_lock) return _takenCount;
        }
    }

    // where a test holds a connect at its take of the tunnel: a take waits for it first
    public Task? TakeGate { get; init; }

    // completes once a take waits at the gate
    public Task TakeWaiting => _takeWaiting.Task;

    // where a test holds a hold's close, as a slow one, before it lets the tunnel go: a close waits for it first
    public Task? HoldDisposeGate { get; set; }

    // completes once a close waits at that gate
    public Task HoldDisposeWaiting => _holdDisposeWaiting.Task;

    // where a test holds a take once it has the tunnel, before it returns its hold, as Windows' mutex thread may: the
    // take waits for it, a cancel too, and returns the hold
    public Task? TakeAcquiredGate { get; set; }

    // completes once a take waits at that gate
    public Task TakeAcquiredWaiting => _takeAcquiredWaiting.Task;

    // completes once a take has asked a holder to let go
    public Task Asked => _asked.Task;

    internal void WaitForHoldDispose()
    {
        if (HoldDisposeGate is not { } holdDisposeGate)
            return;

        _holdDisposeWaiting.TrySetResult();
        holdDisposeGate.Wait(CancellationToken.None);
    }

    public async Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken)
    {
        if (TakeGate is { } takeGate) {
            _takeWaiting.TrySetResult();
            await takeGate.WaitAsync(cancellationToken).Vhc();
        }

        while (true) {
            var (taken, current) = TryTake(holder);
            if (taken != null) {
                if (TakeAcquiredGate is { } takeAcquiredGate) {
                    _takeAcquiredWaiting.TrySetResult();
                    await takeAcquiredGate.Vhc();
                }

                return taken;
            }

            _asked.TrySetResult();
            current?.Ask();
            if (current != null)
                await current.Released.WaitAsync(cancellationToken).Vhc();
        }
    }

    // a hold of the free tunnel, or the current one, which is asked
    private (TestVpnTunnelHold? Taken, TestVpnTunnelHold? Current) TryTake(IVpnTunnelHolder holder)
    {
        lock (_lock) {
            if (_hold != null)
                return (null, _hold);

            _hold = new TestVpnTunnelHold(this, holder);
            _takenCount++;
            return (_hold, null);
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
