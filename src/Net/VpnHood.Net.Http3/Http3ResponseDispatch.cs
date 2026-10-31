using System.Runtime.ExceptionServices;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal sealed class Http3ResponseDispatch(Stream stream, IQuicStream quic,
    IHttp3Responder responder, Http3Request request, Http3HeaderBudget budget, Http3ServerOptions options,
    Action<Exception> onError)
{
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
        if (_completed || _failure != null)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        Select();
        try {
            var response = new Http3ResponseWriter(stream, options);
            // Selection is frozen before inspection continues; native writes cannot change the schedule.
            if (_exceedsRequestLimits)
                await response.WriteHeaders(431, [], cancellationToken).Vhc();
            else
                await responder.Respond(request, response, cancellationToken).Vhc();
            quic.CompleteWrites();
            _completed = true;
            quic.AbortRead(Http3Protocol.NoError);
        }
        catch (Exception ex) {
            Fail(ex);
        }
    }

    public void Fail(Exception error)
    {
        if (_completed || _failure != null)
            return;
        _failure = error;
        onError(error);
    }

    public void ThrowIfFailed(CancellationToken cancellationToken)
    {
        if (_failure != null)
            ExceptionDispatchInfo.Capture(_failure).Throw();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
