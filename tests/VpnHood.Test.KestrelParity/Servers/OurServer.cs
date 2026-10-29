using System.Net;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.KestrelParity.Servers;

// Our server as an admin runs it, with a file access manager, its own self-signed certificate and the HTTP-01
// handler, on loopback. That handler always takes port 80, which another program on this machine must not hold.
internal sealed class OurServer : IAsyncDisposable
{
    private readonly VpnHoodServer _server;

    public IPEndPoint TlsEndPoint { get; }
    public IPEndPoint HttpEndPoint { get; } = new(IPAddress.Loopback, 80);

    // the certificate's name, which each TLS probe sends as its SNI to both servers
    public string HostName { get; }

    // the certificate both servers present, so their handshakes compare
    public string CertificatePath { get; }

    private OurServer(VpnHoodServer server, IPEndPoint tlsEndPoint, string hostName, string certificatePath)
    {
        _server = server;
        TlsEndPoint = tlsEndPoint;
        HostName = hostName;
        CertificatePath = certificatePath;
    }

    public static async Task<OurServer> Start(string storagePath, CancellationToken cancellationToken)
    {
        var tlsEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var accessManager = new FileAccessManager(storagePath, new FileAccessManagerOptions {
            PublicEndPoints = [tlsEndPoint],
            TcpEndPoints = [tlsEndPoint],
            QuicEndPoints = [],
            UdpEndPoints = [],
            UseExternalLocationService = false,
            LogAnonymizer = false,
            EnableHttp01Challenge = true
        });

        var accessToken = accessManager.AccessTokenService.Create(tokenName: "parity", maxClientCount: 1);
        var hostName = accessManager.GetToken(accessToken).ServerToken.HostName;
        var certificatePath = Path.Combine(accessManager.CertsFolderPath, "default.pfx");

        var server = new VpnHoodServer(accessManager, new ServerOptions {
            StoragePath = Path.Combine(storagePath, "server"),
            PublicIpDiscovery = false,
            AutoDisposeAccessManager = true
        });
        await server.Start(cancellationToken);
        return new OurServer(server, tlsEndPoint, hostName, certificatePath);
    }

    public ValueTask DisposeAsync()
    {
        return _server.DisposeAsync();
    }
}
