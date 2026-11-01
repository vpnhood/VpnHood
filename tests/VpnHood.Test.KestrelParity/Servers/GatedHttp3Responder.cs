using VpnHood.Net.Http3;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class GatedHttp3Responder(Http3RequestGate gate, IHttp3Responder responder) : IHttp3Responder
{
    public async Task Respond(Http3Request request, Http3ResponseWriter response, CancellationToken cancellationToken)
    {
        gate.OnApplicationStarted();
        await gate.WaitForPrefixConsumed(cancellationToken);
        await responder.Respond(request, response, cancellationToken);
    }
}
