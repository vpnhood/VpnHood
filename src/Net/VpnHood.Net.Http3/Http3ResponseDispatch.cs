using System.Runtime.ExceptionServices;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal sealed class Http3ResponseDispatch(Stream stream, IQuicStream quic, Http3StreamContext context,
    IHttp3Responder responder, Http3Request request, Http3HeaderBudget budget, Http3ServerOptions options,
    Action<Exception> onError, CancellationToken cancellationToken) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _stateLock = new();
    private Task? _response;
    private Exception? _failure;
    private bool _responseSelected;
    private bool _completed;

    public bool ResponseSelected => Volatile.Read(ref _responseSelected);

    public async Task Start(CancellationToken cancellationToken)
    {
        if (_response == null) {
            Http3RequestMetrics.RequestQueued(context.LocalEndPoint, 1);
            _response = Task.Run(RunQueued);
        }
        // Give the application its dispatch turn before inspecting trailers already in the burst.
        await _started.Task.WaitAsync(cancellationToken).Vhc();
    }

    public async Task Complete(CancellationToken cancellationToken)
    {
        if (_response == null) {
            cancellationToken.ThrowIfCancellationRequested();
            _response = RunResponse(_cancellation.Token);
        }
        await _response.Vhc();
    }

    public void Fail(Exception error)
    {
        lock (_stateLock) {
            // A complete response is the boundary even if its parser was already processing a frame.
            if (_completed || _failure != null)
                return;
            _failure = error;
        }
        // Send the protocol error before cancellation can replace it with the transport's default reset.
        onError(error);
        _cancellation.TryCancel();
    }

    public async Task Join(CancellationToken cancellationToken)
    {
        // The write owns its header deadline; even canceled callers must join it before disposing QUIC.
        if (_response != null)
            await _response.Vhc();
        if (_failure != null)
            ExceptionDispatchInfo.Capture(_failure).Throw();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task RunQueued()
    {
        _started.TrySetResult();
        Http3RequestMetrics.RequestQueued(context.LocalEndPoint, -1);
        await RunResponse(_cancellation.Token).Vhc();
    }

    private async Task RunResponse(CancellationToken cancellationToken)
    {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new Http3ResponseWriter(stream, options);
            // RC3 checks the live totals on application dispatch, before the response is committed.
            var exceedsRequestLimits = budget.ExceedsRequestLimits;
            Volatile.Write(ref _responseSelected, true);
            if (exceedsRequestLimits)
                await response.WriteHeaders(431, [], cancellationToken).Vhc();
            else
                await responder.Respond(request, response, cancellationToken).Vhc();
            lock (_stateLock) {
                if (_failure != null)
                    return;
                quic.CompleteWrites();
                Volatile.Write(ref _completed, true);
            }
            quic.AbortRead(Http3Protocol.NoError);
        }
        catch (Exception ex) {
            Fail(ex);
        }
    }

    public void Dispose() => _cancellation.Dispose();
}
