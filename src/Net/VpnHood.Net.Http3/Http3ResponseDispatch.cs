using System.Runtime.ExceptionServices;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal sealed class Http3ResponseDispatch(Stream stream, IQuicStream quic,
    IHttp3Responder responder, Http3Request request, Http3HeaderBudget budget, Http3ServerOptions options,
    Action<Exception> onError, CancellationToken cancellationToken) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly Lock _stateLock = new();
    private Task? _response;
    private Exception? _failure;
    private bool _responseSelected;
    private bool _exceedsRequestLimits;
    private bool _completed;

    public bool Select()
    {
        if (!_responseSelected) {
            _exceedsRequestLimits = budget.ExceedsRequestLimits;
            _responseSelected = true;
        }
        return _exceedsRequestLimits;
    }

    public async Task Complete(CancellationToken cancellationToken)
    {
        if (_response == null) {
            cancellationToken.ThrowIfCancellationRequested();
            Select();
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

    private async Task RunResponse(CancellationToken cancellationToken)
    {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new Http3ResponseWriter(stream, options);
            // Selection is frozen before inspection continues; native writes cannot change the schedule.
            if (_exceedsRequestLimits)
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
