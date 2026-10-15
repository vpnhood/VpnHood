using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Windows.WinNative;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Windows;

// One app's hold on the tunnel (WindowsVpnTunnelLock). A mutex belongs to the thread that took it, so a
// thread of its own takes it, asking the holder each second to let go meanwhile, then waits for an app's
// ask or this hold's end. An ask goes to the holder, whose adapter is gone once it returns, and the mutex
// goes right after: the asking app waits for nothing else of this one. The names are the SDK's, not an
// app's, so every app built on VpnHood takes part.
internal sealed class WindowsVpnTunnelHold : IDisposable
{
    private const string NamespaceName = "VpnHoodTunnel";
    private const string MutexName = "Tunnel";
    private const string AskName = "LetGo";
    private static readonly TimeSpan AskInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LetGoTimeout = TimeSpan.FromSeconds(30);
    private static readonly Lock NamespaceLock = new();
    private static AdministratorsNamespace? _tunnelNamespace;

    private readonly IVpnTunnelHolder _holder;
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource<IDisposable> _taken = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEvent _ended = new(false);
    private readonly Thread _thread;

    // set by Dispose or by the thread's end, whichever comes first, and that one disposes _ended
    private int _isEnded;

    private WindowsVpnTunnelHold(IVpnTunnelHolder holder, CancellationToken cancellationToken)
    {
        _holder = holder;
        _cancellationToken = cancellationToken;
        _thread = new Thread(Run) { IsBackground = true, Name = "VpnTunnelHold" };
    }

    public static Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken)
    {
        var hold = new WindowsVpnTunnelHold(holder, cancellationToken);
        hold._thread.Start();
        return hold._taken.Task;
    }

    private void Run()
    {
        try {
            var tunnelNamespace = OpenNamespace();
            using var mutex = new Mutex(false, tunnelNamespace.Name(MutexName));
            using var ask = new EventWaitHandle(false, EventResetMode.AutoReset, tunnelNamespace.Name(AskName));
            TakeMutex(mutex, ask);
            try {
                Hold(ask);
            }
            finally {
                mutex.ReleaseMutex();
            }
        }
        catch (Exception ex) {
            // before the take, the hold never was; after it, the mutex could not be let go
            if (!_taken.TrySetException(ex))
                VhLogger.Instance.LogError(ex, "Could not let the tunnel go.");
        }
        finally {
            // a Dispose that came first waits for this end and disposes it itself
            if (Interlocked.Exchange(ref _isEnded, 1) == 0)
                _ended.Dispose();
        }
    }

    // One for the process, kept open for its life: a process may open a namespace's name only once, so a
    // second hold opening it again would fail while the first is open (ERROR_DUP_NAME).
    private static AdministratorsNamespace OpenNamespace()
    {
        lock (NamespaceLock)
            return _tunnelNamespace ??= AdministratorsNamespace.Open(NamespaceName);
    }

    // The mutex, asking its holder each second to let go meanwhile; one a dead holder abandoned is taken
    // as it is. Asks left from before this take were for the last holder, so they go.
    private void TakeMutex(Mutex mutex, EventWaitHandle ask)
    {
        if (!WaitForMutex(mutex, TimeSpan.Zero)) {
            VhLogger.Instance.LogInformation("Another VPN app holds the tunnel; asking it to let go...");
            var stopwatch = Stopwatch.StartNew();
            do {
                if (stopwatch.Elapsed > LetGoTimeout)
                    throw new TimeoutException("Another VPN app holds the tunnel and did not let it go.");

                ask.Set();
            } while (!WaitForMutex(mutex, AskInterval));
        }

        ask.Reset();
    }

    // Whether the mutex is this thread's now. A cancellation ends the wait at once, but one that comes with
    // the mutex leaves it taken, for the caller's hold to let go.
    private bool WaitForMutex(Mutex mutex, TimeSpan timeout)
    {
        try {
            if (WaitHandle.WaitAny([mutex, _cancellationToken.WaitHandle], timeout) == 0)
                return true;
        }
        catch (AbandonedMutexException) {
            VhLogger.Instance.LogWarning("The tunnel's last holder ended without letting it go.");
            return true;
        }

        _cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    // Until an app asks for the tunnel or this hold ends. An ask goes to the holder, once, unless the hold
    // ended meanwhile; the mutex goes when this returns.
    private void Hold(EventWaitHandle ask)
    {
        _taken.TrySetResult(this);
        if (WaitHandle.WaitAny([ask, _ended]) != 0 || Volatile.Read(ref _isEnded) == 1)
            return;

        try {
            _holder.OnTunnelRevoked();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not tell the tunnel's holder that another app asked for it.");
        }
    }

    // Waits for the thread to let the mutex go, so the next app's take follows this hold's end; on the
    // thread itself, as when the holder lets go from within its OnTunnelRevoked, the thread does it next.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isEnded, 1) == 1)
            return;

        _ended.Set();
        if (Thread.CurrentThread != _thread)
            _thread.Join();

        _ended.Dispose();
    }
}
