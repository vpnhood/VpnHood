using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Cli.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Channel;

// The service's side of the channel. An administrator's connection is kept, so the next address
// reaches it; any other is closed once answered, and a silent one after a few seconds.
internal sealed class DaemonChannelServer : IAsyncDisposable
{
    private const int MaxHelloLength = 4096;
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan AcceptWarningInterval = TimeSpan.FromMinutes(1);

    private readonly IDaemonChannelListener _listener;
    private readonly string _refusal;
    private readonly Lock _lock = new();
    private readonly List<Connected> _connected = [];
    private readonly HashSet<string> _refusedCallers = [];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _stopping;
    private readonly Task _serving;
    private DaemonChannelAnswer _current;
    private DateTime _acceptWarnedAt = DateTime.MinValue;
    private bool _disposed;

    private DaemonChannelServer(IDaemonChannelListener listener, DaemonChannelAnswer answer, string refusal)
    {
        _listener = listener;
        _current = answer;
        _refusal = refusal;
        _stopping = _cancellation.Token; // kept: a send may start after the source is disposed
        _serving = Serve(_stopping);
    }

    // Null when the channel could not be opened, which is logged: the tunnel stays up without it,
    // and "service restart" tries again.
    public static async Task<DaemonChannelServer?> TryStart(IDaemonChannel channel, DaemonChannelAnswer answer,
        string refusal, CancellationToken cancellationToken)
    {
        try {
            var listener = await channel.Listen(cancellationToken).Vhc();
            return new DaemonChannelServer(listener, answer, refusal);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            VhLogger.Instance.LogError(ex, "Could not open the channel the window and the commands ask for the API on. Restart the service to try again.");
            return null;
        }
    }

    // A new address or token, for everyone connected and whoever asks next.
    public void Publish(DaemonChannelAnswer answer)
    {
        foreach (var connected in Replace(answer))
            _ = connected.SendCurrent(_stopping);
    }

    // Who must hear of the answer: nobody when it is the same again, or when the service is stopping.
    private Connected[] Replace(DaemonChannelAnswer answer)
    {
        lock (_lock) {
            if (_disposed || IsSame(answer, _current))
                return [];

            _current = answer;
            return [.. _connected];
        }
    }

    private DaemonChannelAnswer Current {
        get {
            lock (_lock)
                return _current;
        }
    }

    // By the address as written: the token is its fragment, which Uri.Equals ignores.
    private static bool IsSame(DaemonChannelAnswer answer, DaemonChannelAnswer other)
    {
        return answer.ProcessId == other.ProcessId && answer.ApiUrl?.OriginalString == other.ApiUrl?.OriginalString;
    }

    private async Task Serve(CancellationToken cancellationToken)
    {
        try {
            while (true) {
                if (await TryAccept(cancellationToken).Vhc() is { } caller)
                    _ = Handle(caller, cancellationToken);
                else
                    await Task.Delay(AcceptRetryDelay, cancellationToken).Vhc();
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) {
            // the service is stopping
        }
    }

    // Null when an accept failed and the listener stays open, as with no handle left. Callers may
    // cause such failures at will, so a warning comes once a minute at most.
    private async Task<IDaemonChannelCaller?> TryAccept(CancellationToken cancellationToken)
    {
        try {
            return await _listener.Accept(cancellationToken).Vhc();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            var warn = DateTime.UtcNow - _acceptWarnedAt >= AcceptWarningInterval;
            if (warn)
                _acceptWarnedAt = DateTime.UtcNow;

            VhLogger.Instance.Log(warn ? LogLevel.Warning : LogLevel.Debug, ex,
                "The channel could not take a caller; it goes on listening.");
            return null;
        }
    }

    private async Task Handle(IDaemonChannelCaller caller, CancellationToken cancellationToken)
    {
        Connected? connected = null;
        try {
            // A caller that does not say it is one, soon and briefly, holds an instance for nobody.
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            helloTimeout.CancelAfter(HelloTimeout);
            var hello = await DaemonChannelLines.ReadBounded<DaemonChannelHello>(caller.Stream, MaxHelloLength,
                helloTimeout.Token).Vhc();
            if (hello == null)
                return;

            var identity = caller.Identify();
            if (!identity.IsAdministrator) {
                // at Information once per caller: anyone may ask as often as they like
                VhLogger.Instance.Log(IsFirstRefusal(identity.Name) ? LogLevel.Information : LogLevel.Debug,
                    "{Caller} asked for the API and is not an administrator.", identity.Name);
                await DaemonChannelLines.Write(caller.Stream,
                    new DaemonChannelAnswer { Version = DaemonChannelAnswer.CurrentVersion, Refusal = _refusal },
                    cancellationToken).Vhc();
                return;
            }

            VhLogger.Instance.LogDebug("{Caller} (version {Version}) is connected to the channel.", identity.Name, hello.Version);
            connected = new Connected(this, caller);
            lock (_lock)
                _connected.Add(connected);

            await connected.SendCurrent(cancellationToken).Vhc();
            await WaitForEnd(caller.Stream, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // the service is stopping
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "A channel connection ended with an error.");
        }
        finally {
            if (connected != null)
                lock (_lock)
                    _connected.Remove(connected);

            await caller.DisposeAsync().Vhc();
        }
    }

    private bool IsFirstRefusal(string caller)
    {
        lock (_lock)
            return _refusedCallers.Add(caller);
    }

    // The caller sends nothing more, so whatever comes is read and dropped until the connection ends.
    private static async Task WaitForEnd(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        while (await stream.ReadAsync(buffer, cancellationToken).Vhc() > 0) {
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock) {
            if (_disposed)
                return;
            _disposed = true;
        }

        await _cancellation.CancelAsync().Vhc();
        await _serving.Vhc();
        await _listener.DisposeAsync().Vhc();
        _cancellation.Dispose();
    }

    // An administrator's held connection. Its writes go one at a time, each of whatever is current
    // then, so two quick changes cannot leave it on the older one.
    private sealed class Connected(DaemonChannelServer server, IDaemonChannelCaller caller)
    {
        private readonly SemaphoreSlim _writing = new(1, 1);
        private DaemonChannelAnswer? _sent;

        public async Task SendCurrent(CancellationToken cancellationToken)
        {
            try {
                await _writing.WaitAsync(cancellationToken).Vhc();
                try {
                    var current = server.Current;
                    if (!ReferenceEquals(current, _sent)) {
                        await DaemonChannelLines.Write(caller.Stream, current, cancellationToken).Vhc();
                        _sent = current;
                    }
                }
                finally {
                    _writing.Release();
                }
            }
            catch (Exception ex) {
                // the caller or the service is going, and Handle sees the end
                if (!cancellationToken.IsCancellationRequested)
                    VhLogger.Instance.LogDebug(ex, "Could not send the address over a channel connection.");
            }
        }
    }
}
