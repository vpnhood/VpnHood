using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// The host's hold of the machine's one tunnel, where the platform has one (IVpnTunnelLock). A connect takes it before
// its adapter comes up, the host keeps it across its own reconnects, and a session's end or another app's ask ends it.
// Takes and releases run one after another: a take that met this host's own hold would ask it as another app's, and
// the answer would revoke the connect that takes.
internal class VpnServiceTunnel(IVpnTunnelLock? tunnelLock, IVpnTunnelHolder holder, VpnServiceContext context)
    : IDisposable
{
    private IDisposable? _hold;
    private Task _released = Task.CompletedTask; // the last hold's end, or take's, which the next take waits for
    private readonly TaskCompletionSource _takeWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // whether the tunnel, and so the kill switch's block, is this host's: always true where the platform has no lock
    public bool IsHeld => tunnelLock == null || _hold != null;

    // for tests: set when a take starts waiting for an earlier take or release
    public Task TakeWaiting => _takeWaiting.Task;

    // for tests: set when a session's end starts waiting for a hold that another end is closing
    public Task ReleaseWaiting => _releaseWaiting.Task;

    // Takes the tunnel for a connect, unless the host holds it already. A connect that a newer one took over gives the
    // hold back at once: the newer one, which found no hold, asks for it, and a hold kept here would take that for
    // another app's ask
    public async Task EnsureTaken(int sessionId, CancellationToken cancellationToken)
    {
        if (tunnelLock == null || _hold != null)
            return;

        var lastRelease = BeginTake(out var taken);
        try {
            if (!lastRelease.IsCompleted)
                _takeWaiting.TrySetResult();

            await lastRelease.WaitAsync(cancellationToken).Vhc();
            var hold = await tunnelLock.Take(holder, cancellationToken).Vhc();
            lock (context.SessionLock) {
                if (context.SessionId == sessionId) {
                    _hold = hold;
                    return;
                }
            }

            VhInvoker.TryInvoke("let the tunnel go", LogLevel.Error, hold.Dispose);
            throw new OperationCanceledException("A newer connect has taken over the VPN service.");
        }
        finally {
            taken.TrySetResult();
        }
    }

    // Releases the tunnel at a session's end, once no adapter of the session is up, and waits for every release so far,
    // as the app may take the tunnel at the end. Returns false if a newer connect had begun when the hold was taken
    // away: it keeps the hold, as across reconnects. One that begins later finds no hold, and waits for this release
    public async Task<bool> TryRelease(int sessionId)
    {
        if (TryTakeHold(sessionId) is not { } release)
            return false;

        // outside the lock: a hold's end waits for an ask it hands the host, which takes the lock
        if (release.TunnelHold is { } hold) {
            VhInvoker.TryInvoke("let the tunnel go", LogLevel.Error, hold.Dispose);
            release.Released.TrySetResult();
        }
        else if (!release.AllReleased.IsCompleted) {
            _releaseWaiting.TrySetResult();
        }

        await release.AllReleased.Vhc();
        return true;
    }

    // another app took the tunnel: the hold ends by itself, so the host only forgets it, and a reconnect takes it anew
    public void Forget()
    {
        Interlocked.Exchange(ref _hold, null);
    }

    // a take's turn: the last hold's or take's end, which it waits for, and its own, which the next take waits for
    private Task BeginTake(out TaskCompletionSource taken)
    {
        lock (context.SessionLock) {
            var lastRelease = _released;
            taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _released = AddEnd(lastRelease, taken.Task);
            return lastRelease;
        }
    }

    // The session's hold, taken from the host under the session lock: a newer connect either began before and keeps
    // the hold, or begins after, finds none, and waits for this release before it takes the tunnel
    private TunnelRelease? TryTakeHold(int sessionId)
    {
        lock (context.SessionLock) {
            if (context.SessionId != sessionId)
                return null;

            var hold = _hold;
            _hold = null;
            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (hold != null)
                _released = AddEnd(_released, released.Task);

            return new TunnelRelease { TunnelHold = hold, Released = released, AllReleased = _released };
        }
    }

    // the ends so far and one more: the one alone where those so far have all come
    private static Task AddEnd(Task ends, Task end)
    {
        return ends.IsCompleted ? end : Task.WhenAll(ends, end);
    }

    // the host's end, once no adapter of it is up: the hold goes, where there is one
    public void Dispose()
    {
        Interlocked.Exchange(ref _hold, null)?.Dispose();
    }
}
