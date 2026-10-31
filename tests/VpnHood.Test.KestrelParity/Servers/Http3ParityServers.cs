using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Utils;
using SessionOptions = VpnHood.Core.Server.Access.Configurations.SessionOptions;

namespace VpnHood.Test.KestrelParity.Servers;

// The real server and an ordinary protected HTTP/3 application, with the same certificate and QUIC runtime.
// Separate ports avoid routing tricks in the client; no HTTP-01 listener or external service is needed.
internal sealed class Http3ParityServers(
    VpnHoodServer server, WebApplication reference, X509Certificate2 certificate,
    IPEndPoint referenceEndPoint, string hostName, byte[] farmSecret) : IAsyncDisposable
{
    public IPEndPoint OurEndPoint => server.ServerHost.QuicEndPoints.Single();
    public IPEndPoint ReferenceEndPoint => referenceEndPoint;
    public X509Certificate2 Certificate => certificate;
    public string HostName => hostName;
    public byte[] FarmSecret => farmSecret;

    public static async Task<Http3ParityServers> Start(string storagePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(storagePath);
        var tcpEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var quicEndPoint = VhUtils.GetFreeUdpEndPoint(IPAddress.Loopback);
        var accessManager = await FileAccessManager.Create(storagePath, new FileAccessManagerOptions {
            PublicEndPoints = [tcpEndPoint],
            TcpEndPoints = [tcpEndPoint],
            QuicEndPoints = [quicEndPoint],
            UdpEndPoints = [],
            ReverseProxyProviders = [],
            EnableHttp01Challenge = false,
            UseExternalLocationService = false,
            LogAnonymizer = false,
            SessionOptions = new SessionOptions { MinClientProtocolVersion = 15 }
        }, cancellationToken);
        var server = CreateServer(accessManager, storagePath);
        try {
            var referenceEndPoint = VhUtils.GetFreeUdpEndPoint(IPAddress.Loopback);
            var accessToken = accessManager.AccessTokenService.Create(tokenName: "http3-parity", maxClientCount: 1);
            var token = accessManager.GetToken(accessToken).ServerToken;
            var farmSecret = token.Secret ?? throw new InvalidOperationException("The test token has no farm secret.");
            await server.Start(cancellationToken);
            Assert.HasCount(1, server.ServerHost.QuicEndPoints);
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                Path.Combine(accessManager.CertsFolderPath, "default.pfx"), null);
            try {
                var reference = await StartReference(certificate, referenceEndPoint, null, cancellationToken);
                return new Http3ParityServers(server, reference, certificate, referenceEndPoint, token.HostName,
                    farmSecret);
            }
            catch {
                certificate.Dispose();
                throw;
            }
        }
        catch {
            await server.DisposeAsync();
            throw;
        }
    }

    private static VpnHoodServer CreateServer(FileAccessManager accessManager, string storagePath)
    {
        try {
            return new VpnHoodServer(accessManager, new ServerOptions {
                StoragePath = Path.Combine(storagePath, "server"),
                PublicIpDiscovery = false,
                AutoDisposeAccessManager = true,
                QuicServer = new MsQuicServer()
            });
        }
        catch {
            accessManager.Dispose();
            throw;
        }
    }

    internal static async Task<WebApplication> StartReference(X509Certificate2 certificate, IPEndPoint endPoint,
        ReferenceRequestGate? referenceGate, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => {
            if (referenceGate != null)
                options.RequestHeaderEncodingSelector = referenceGate.SelectEncoding;
            options.Listen(endPoint, listen => {
                listen.Protocols = HttpProtocols.Http3;
                listen.UseHttps(certificate);
            });
        });
        var app = builder.Build();
        app.Run(async context => {
            // Only timing probes hold the application so the frame parser must choose the first error.
            if (context.Request.Headers.ContainsKey("x-parity-wait-for-abort"))
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            context.Response.StatusCode = 401;
            context.Response.Headers.WWWAuthenticate = "Bearer";
        });
        try {
            await app.StartAsync(cancellationToken);
            return app;
        }
        catch {
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var errors = new List<Exception>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try {
            await reference.StopAsync(stop.Token);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }
        // A failed reference close must not leave our listener running for the next test class.
        foreach (var resource in new IAsyncDisposable[] { reference, server }) {
            try {
                await resource.DisposeAsync();
            }
            catch (Exception ex) {
                errors.Add(ex);
            }
        }
        certificate.Dispose();
        if (errors.Count > 0)
            throw new AggregateException(errors);
    }
}
