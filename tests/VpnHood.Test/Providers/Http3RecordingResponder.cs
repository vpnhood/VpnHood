using VpnHood.Net.Http3;

namespace VpnHood.Test.Providers;

internal sealed class Http3RecordingResponder : IHttp3Responder
{
    public Http3Request? Request { get; private set; }

    public Task Respond(Http3Request request, Http3ResponseWriter response, CancellationToken cancellationToken)
    {
        Request = request;
        return response.WriteHeaders(401, [], cancellationToken);
    }
}
