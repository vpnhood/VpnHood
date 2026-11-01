using VpnHood.Net.Http3;

namespace VpnHood.Test.Providers;

internal sealed class Http3GatedResponder(Http3RequestTestGate gate, bool waitForInput = true) : IHttp3Responder
{
    public async Task Respond(Http3Request request, Http3ResponseWriter response, CancellationToken cancellationToken)
    {
        gate.ApplicationStarted();
        if (waitForInput)
            await gate.WaitForInput(cancellationToken);
        await response.WriteHeaders(401, [], cancellationToken);
    }
}
