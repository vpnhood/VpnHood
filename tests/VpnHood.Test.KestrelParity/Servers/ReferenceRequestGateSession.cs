using System.Runtime.ExceptionServices;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class ReferenceRequestGateSession : IDisposable
{
    private readonly string _markerName;
    private readonly TimeSpan _timeout;
    private readonly Action<ReferenceRequestGateSession> _disarm;
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationToken _deadlineToken;
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ExceptionDispatchInfo? _failure;
    private int _dispatches;
    private int _disposed;

    public ReferenceRequestGateSession(string markerName, TimeSpan timeout,
        Action<ReferenceRequestGateSession> disarm, CancellationToken cancellationToken)
    {
        _markerName = markerName;
        _timeout = timeout;
        _disarm = disarm;
        _deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _deadline.CancelAfter(timeout);
        _deadlineToken = _deadline.Token;
    }

    public Task WaitForDispatch(CancellationToken cancellationToken) =>
        Wait(_entered.Task, "The server did not reach its application dispatch gate.", cancellationToken);

    public bool WasEntered => _entered.Task.IsCompletedSuccessfully;

    public async Task WaitForCompletion(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Cleanup must still join the callback after the request's deadline has expired.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(_timeout);
        try {
            await _completed.Task.WaitAsync(lifetime.Token);
        }
        catch (OperationCanceledException ex) {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("The server did not leave its application dispatch gate.", ex);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Read(ref _failure)?.Throw();
    }

    public void Release() => _released.TrySetResult();

    public void OnHeader(string name)
    {
        if (name == _markerName)
            Release();
    }

    public void OnDispatch(CancellationToken cancellationToken)
    {
        // RC3 dequeues parsing before application dispatch, so only the second event holds the application.
        if (Interlocked.Increment(ref _dispatches) != 2)
            return;
        _entered.TrySetResult();
        try {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadlineToken);
            // MeterListener is synchronous by contract, so returning would release application dispatch.
            _released.Task.WaitAsync(lifetime.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException ex) {
            Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(
                new TimeoutException("The application dispatch gate was not released.", ex)));
        }
        catch (Exception ex) {
            // A test failure must never escape through the server's instrumentation callback.
            Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(ex));
        }
        finally {
            _completed.TrySetResult();
        }
    }

    private async Task Wait(Task task, string timeoutMessage, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadlineToken);
        try {
            await task.WaitAsync(lifetime.Token);
        }
        catch (OperationCanceledException ex) {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(timeoutMessage, ex);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Release();
        _disarm(this);
        _deadline.Dispose();
    }
}
