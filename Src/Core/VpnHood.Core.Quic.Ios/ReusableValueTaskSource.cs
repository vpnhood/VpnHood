using System.Threading.Tasks.Sources;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Reusable, allocation-free bridge from a callback-based operation to an awaited <see cref="ValueTask"/>
/// (no result). Use <see cref="ReusableValueTaskSource{T}"/> when the operation produces a result.
/// </summary>
/// <remarks>
/// One in-flight operation per instance — intended for a serial consumer, so a single source can be reused
/// across calls via <see cref="Reset"/> instead of allocating per call. The <see cref="_state"/> guard makes
/// completion idempotent, which is what protects against two completion paths racing (e.g. the operation's
/// own callback vs. a cancellation/dispose).
/// </remarks>
public sealed class ReusableValueTaskSource : IValueTaskSource
{
    // RunContinuationsAsynchronously = true is REQUIRED: completion is signalled from a Network.framework
    // callback running on the QUIC stream's serial dispatch queue. With the default (false) the awaiting
    // continuation (the whole proxy read->write pump) would run INLINE on that queue, blocking the stream's
    // own subsequent receive/state/Cancel callbacks -> stalls and connection instability under load. True
    // hands the continuation to the thread pool, freeing the callback thread immediately.
    private ManualResetValueTaskSourceCore<bool> _core = new() { RunContinuationsAsynchronously = true };
    private int _state;

    public short Version => _core.Version;

    public void Reset()
    {
        _state = 0;
        _core.Reset();
    }

    public bool TrySetResult()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) {
            _core.SetResult(true);
            return true;
        }
        return false;
    }

    public bool TrySetException(Exception ex)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) {
            _core.SetException(ex);
            return true;
        }
        return false;
    }

    // Two-phase completion: claim the slot first, do side effects while still uncompleted, then publish.
    // Lets a callback gate work that must NOT run if another path (cancellation/dispose) already completed
    // this operation. After TryReserve() returns true, exactly one SetReserved* call must follow.
    public bool TryReserve() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    public void SetReservedResult() => _core.SetResult(true);
    public void SetReservedException(Exception ex) => _core.SetException(ex);

    public void GetResult(short token) => _core.GetResult(token);

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);
}

/// <summary>
/// Reusable, allocation-free bridge from a callback-based operation to an awaited
/// <see cref="ValueTask{TResult}"/> carrying a result. See <see cref="ReusableValueTaskSource"/> for the
/// no-result variant and the shared usage/threading notes.
/// </summary>
public sealed class ReusableValueTaskSource<T> : IValueTaskSource<T>
{
    // See ReusableValueTaskSource for why RunContinuationsAsynchronously must be true: completion is raised
    // from a Network.framework callback on the QUIC stream's serial dispatch queue, and running the
    // continuation inline there starves the stream's other callbacks and destabilizes the connection.
    private ManualResetValueTaskSourceCore<T> _core = new() { RunContinuationsAsynchronously = true };
    private int _state;

    public short Version => _core.Version;

    public void Reset()
    {
        _state = 0;
        _core.Reset();
    }

    public bool TrySetResult(T result)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) {
            _core.SetResult(result);
            return true;
        }
        return false;
    }

    public bool TrySetException(Exception ex)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) {
            _core.SetException(ex);
            return true;
        }
        return false;
    }

    // Two-phase completion: claim the slot first, do side effects while still uncompleted, then publish.
    // Lets a callback gate work that must NOT run if another path (cancellation/dispose) already completed
    // this operation. After TryReserve() returns true, exactly one SetReserved* call must follow.
    public bool TryReserve() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    public void SetReservedResult(T result) => _core.SetResult(result);
    public void SetReservedException(Exception ex) => _core.SetException(ex);

    public T GetResult(short token) => _core.GetResult(token);

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);
}
