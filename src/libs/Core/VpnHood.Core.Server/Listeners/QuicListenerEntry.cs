using Microsoft.Extensions.Logging;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.Listeners;

internal sealed class QuicListenerEntry : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly HashSet<Task> _connections = [];
    private readonly AsyncLock _listenerCloseLock = new();
    private readonly AsyncLock _disposeLock = new();
    private bool _listenerClosed;
    private bool _disposed;
    private volatile bool _isListening = true;

    public QuicListenerEntry(IQuicListener listener, CancellationToken cancellationToken)
    {
        Listener = listener;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Token = _cancellation.Token;
    }

    public IQuicListener Listener { get; }
    public CancellationToken Token { get; }
    public Task ListenerTask { get; set; } = Task.CompletedTask;
    public bool IsListening => _isListening;

    public void TrackConnection(Task task)
    {
        lock (_connections)
            _connections.Add(task);

        // Remove completed tasks even when no later client connects to this listener.
        _ = task.ContinueWith(static (completed, state) => {
            if (state is QuicListenerEntry entry) {
                lock (entry._connections)
                    entry._connections.Remove(completed);
            }
            if (completed.Exception is { } error)
                VhLogger.Instance.LogError(error, "Error in closing a QUIC connection.");
        }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task WaitForConnections(CancellationToken cancellationToken)
    {
        // Acceptance has ended, so the snapshot cannot miss a new connection.
        lock (_connections)
            return Task.WhenAll(_connections).WaitAsync(cancellationToken);
    }

    public void ReleaseCancellation() => _cancellation.Dispose();

    public async ValueTask CloseListener(CancellationToken cancellationToken)
    {
        _isListening = false;
        using var scopeLock = await _listenerCloseLock.LockAsync(cancellationToken).Vhc();
        if (Interlocked.Exchange(ref _listenerClosed, true))
            return;
        await _cancellation.TryCancelAsync().Vhc();
        await Listener.TryDisposeAsync().Vhc();
    }

    public async ValueTask DisposeAsync()
    {
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;
        await CloseListener(CancellationToken.None).Vhc();
        try { await ListenerTask.Vhc(); }
        catch (OperationCanceledException) { /* ours, cancelled above */ }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in stopping QuicListener.");
        }
    }
}
