using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class GatedHttp3Reference(WebApplication application, IPEndPoint endPoint,
    ReferenceRequestGate gate) : IAsyncDisposable
{
    public IPEndPoint EndPoint => endPoint;
    public ReferenceRequestGate Gate => gate;

    public static async Task<GatedHttp3Reference> Start(X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        var endPoint = VhUtils.GetFreeUdpEndPoint(IPAddress.Loopback);
        var gate = new ReferenceRequestGate(endPoint, Http3Probe.Timeout);
        try {
            var application = await Http3ParityServers.StartReference(certificate, endPoint, gate, cancellationToken);
            return new GatedHttp3Reference(application, endPoint, gate);
        }
        catch {
            gate.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Release any held dispatch before asking Kestrel to finish its requests.
        gate.Dispose();
        var errors = new List<Exception>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try {
            await application.StopAsync(stop.Token);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }
        finally {
            try {
                await application.DisposeAsync();
            }
            catch (Exception ex) {
                errors.Add(ex);
            }
        }
        if (errors.Count > 0)
            throw new AggregateException(errors);
    }
}
