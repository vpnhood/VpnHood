using System.Runtime.ExceptionServices;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

// The parser and application run independently. Async disposal joins application, native write and
// abort work before the request scope can release its reader or native stream.
internal sealed class Http3ResponseDispatch(IQuicStream quic,
    IHttp3Responder responder, Http3Request request, Http3HeaderBudget budget, Http3ServerOptions options,
    Action<Exception> onError, Http3StreamReader reader, Http3RequestHeaders headers) : IAsyncDisposable
{
    private readonly Lock _stateLock = new();
    private CancellationTokenSource? _writeCancellation;
    private CancellationTokenRegistration _headerCancellation;
    private Task? _responseTask;
    private Task? _abortTask;
    private Exception? _failure;
    private bool _completed;
    private bool _aborting;
    private bool _joined;

    // The application/native-write task can finish before the parser. The owner still calls Join
    // after parsing so protocol errors discovered in that interval are not lost.
    internal Task ResponseCompleted => _responseTask ??
        throw new InvalidOperationException("The response has not been queued.");

    public void Start(CancellationToken cancellationToken)
    {
        if (_responseTask != null)
            throw new InvalidOperationException("The response has already been queued.");
        var writeCancellation = new CancellationTokenSource();
        _writeCancellation = writeCancellation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _responseTask = completion.Task;
        // Put the HTTP/3 error on wire before canceling native I/O. Directly linking the native
        // write token would let the transport send its default cancellation code first.
        _headerCancellation = cancellationToken.UnsafeRegister(static (state, token) => {
            if (state is Http3ResponseDispatch dispatch)
                dispatch.Fail(new OperationCanceledException(token));
        }, this);
        // Kestrel's Http3Stream queues application processing after completed HEADERS. Do not await a
        // startup signal here: buffered body/trailer parsing must race that application work.
        try {
            if (!ThreadPool.UnsafeQueueUserWorkItem(static state => {
                    _ = state.Dispatch.CompleteQueued(state.Completion, state.CancellationToken);
                }, (Dispatch: this, Completion: completion, CancellationToken: writeCancellation.Token),
                preferLocal: false))
                throw new InvalidOperationException("The response could not be queued.");
        }
        catch {
            // A failed enqueue leaves no worker to complete the task that the owner must join.
            completion.TrySetResult();
            throw;
        }
    }

    private async Task CompleteQueued(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        // This is the thread-pool entry point. Its owned completion task carries every outcome;
        // the callback itself must never produce an unobserved exception or an async-void failure.
        try {
            await Complete(cancellationToken).Vhc();
            completion.TrySetResult();
        }
        catch (OperationCanceledException ex) {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex) {
            completion.TrySetException(ex);
        }
    }

    private async Task Complete(CancellationToken cancellationToken)
    {
        Task? write = null;
        try {
            lock (_stateLock) {
                if (_completed || _aborting)
                    return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var response = new Http3ResponseWriter(options);
            // Selection uses the totals when the application runs. A later parser error can still
            // win while the transport is sending the response bytes and FIN.
            await Respond(response, cancellationToken).Vhc();
            lock (_stateLock) {
                if (_aborting)
                    return;
                if (response.BufferedResponse is { } bytes)
                    write = quic.WriteFinal(bytes, cancellationToken).AsTask();
                else
                    quic.CompleteWrites();
                // Submission starts draining bytes already available to the parser. A pending read
                // still stops at once; the native write remains owned until it is joined below.
            }
            reader.ResponseSubmitted();
        }
        catch (OperationCanceledException ex) {
            Fail(ex);
        }
        catch (Exception ex) {
            Fail(ex);
        }
        finally {
            try {
                if (write != null)
                    await write.Vhc();
            }
            catch (OperationCanceledException ex) {
                Fail(ex);
            }
            catch (Exception ex) {
                Fail(ex);
            }
            bool completeReader;
            lock (_stateLock)
                completeReader = !_aborting;
            if (completeReader) {
                // A completed native write releases a stalled read promptly. The parser may still
                // consume bytes returned synchronously until it reaches the next pending read.
                try {
                    reader.CompleteResponse();
                }
                catch (OperationCanceledException ex) {
                    Fail(ex);
                }
                catch (Exception ex) {
                    Fail(ex);
                }
            }
            // A final trailer error remains eligible until the reader has actually stopped.
            lock (_stateLock)
                _completed = true;
        }
    }

    private async Task Respond(Http3ResponseWriter response, CancellationToken cancellationToken)
    {
        headers.ValidateRequest();
        // The reference validates request metadata before consulting the live header/trailer totals.
        if (budget.ExceedsRequestLimits)
            await response.WriteHeaders(431, [], cancellationToken).Vhc();
        else
            await responder.Respond(request, response, cancellationToken).Vhc();
    }

    public void Fail(Exception error)
    {
        TaskCompletionSource completion;
        lock (_stateLock) {
            // Native FIN can finish before the parser reaches the last buffered trailer field.
            // Parser errors remain eligible until Join seals the request; a later deadline cannot
            // undo the already completed response.
            if (_joined || _failure != null ||
                _completed && error is not (Http3ProtocolException or Http3QpackException or EndOfStreamException))
                return;
            _failure = error;
            _aborting = true;
            // Publish ownership with the failure. Join must also observe an abort that has not yet
            // entered the transport callback, including a deadline racing the final native write.
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _abortTask = completion.Task;
        }
        Abort(error, completion);
    }

    private void Abort(Exception error, TaskCompletionSource completion)
    {
        // Preserve the protocol error on wire before cancellation can trigger a default native reset.
        try {
            try {
                onError(error);
            }
            finally {
                _writeCancellation?.TryCancel();
                reader.StopReading();
            }
            completion.TrySetResult();
        }
        catch (Exception ex) {
            completion.TrySetException(ex);
        }
    }

    public async Task Join(CancellationToken cancellationToken)
    {
        try {
            await JoinWork(cancellationToken).Vhc();
        }
        finally {
            // A parser failure remains the result when its canceled write also canceled the worker.
            ThrowIfFailed(cancellationToken);
        }
    }

    private async Task JoinWork(CancellationToken cancellationToken)
    {
        try {
            if (_responseTask != null)
                await _responseTask.Vhc();
        }
        finally {
            Task? abort;
            lock (_stateLock) {
                _joined = true;
                abort = _abortTask;
            }
            // Ownership cannot end on caller cancellation while native work still uses this stream.
            if (abort != null)
                await abort.Vhc();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void ThrowIfFailed(CancellationToken cancellationToken)
    {
        if (_failure != null)
            ExceptionDispatchInfo.Capture(_failure).Throw();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask DisposeAsync()
    {
        try {
            // A close must finish native work even if the request's deadline has already expired.
            await JoinWork(CancellationToken.None).Vhc();
        }
        catch (Exception) {
            // Join reports the request failure. Disposal observes every completion without letting a
            // failed transport callback replace that failure while the request scope unwinds.
        }
        finally {
            _headerCancellation.Dispose();
            _writeCancellation?.Dispose();
        }
    }
}
