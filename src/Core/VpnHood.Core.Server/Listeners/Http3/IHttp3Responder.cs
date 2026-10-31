namespace VpnHood.Net.Http3;

public interface IHttp3Responder
{
    Task Respond(Http3Request request, Http3ResponseWriter response, CancellationToken cancellationToken);
}
