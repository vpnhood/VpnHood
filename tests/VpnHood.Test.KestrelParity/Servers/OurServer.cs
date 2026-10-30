using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.KestrelParity.Servers;

// Our server as an admin runs it, with a file access manager, its own self-signed certificate and the HTTP-01
// handler, on loopback. That handler always takes port 80, which another program on this machine must not hold.
internal sealed class OurServer : IAsyncDisposable
{
    // fixed in the handler's code
    private static readonly IPEndPoint Http01EndPoint = new(IPAddress.Loopback, 80);

    private readonly VpnHoodServer _server;

    public IPEndPoint TlsEndPoint { get; }
    public IPEndPoint HttpEndPoint => Http01EndPoint;

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
        // Where the handler can't take port 80 it logs and goes on without it, and every "-http" probe would measure
        // whatever holds the port, another run's server too: the port must be free before the start, and after it
        // redirect to our own TLS port.
        if (!IsFree(Http01EndPoint))
            throw new InvalidOperationException(
                $"{Http01EndPoint} is in use; the parity tests need it for our server's HTTP-01 handler.");

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

        var server = CreateServer(accessManager, storagePath);
        try {
            var accessToken = accessManager.AccessTokenService.Create(tokenName: "parity", maxClientCount: 1);
            var hostName = accessManager.GetToken(accessToken).ServerToken.HostName;
            var certificatePath = Path.Combine(accessManager.CertsFolderPath, "default.pfx");
            await server.Start(cancellationToken);
            await RequireOwnRedirect(tlsEndPoint.Port, cancellationToken);
            return new OurServer(server, tlsEndPoint, hostName, certificatePath);
        }
        catch {
            await server.DisposeAsync();
            throw;
        }
    }

    // the server owns the access manager once it exists, and disposes it with itself
    private static VpnHoodServer CreateServer(FileAccessManager accessManager, string storagePath)
    {
        try {
            return new VpnHoodServer(accessManager, new ServerOptions {
                StoragePath = Path.Combine(storagePath, "server"),
                PublicIpDiscovery = false,
                AutoDisposeAccessManager = true
            });
        }
        catch {
            accessManager.Dispose();
            throw;
        }
    }

    private static bool IsFree(IPEndPoint endPoint)
    {
        return VhUtils.GetFreeTcpEndPoint(endPoint.Address, endPoint.Port).Port == endPoint.Port;
    }

    private static async Task RequireOwnRedirect(int tlsPort, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcpClient = new TcpClient();
        var reply = new StringBuilder();
        try {
            await tcpClient.ConnectAsync(Http01EndPoint, timeoutCts.Token);
            var stream = tcpClient.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n"), timeoutCts.Token);
            var buffer = new byte[4096];
            while (!reply.ToString().Contains("\r\n\r\n", StringComparison.Ordinal)) {
                var read = await stream.ReadAsync(buffer, timeoutCts.Token);
                if (read == 0)
                    break;
                reply.Append(Encoding.Latin1.GetString(buffer, 0, read));
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException ||
                                   (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)) {
            throw new InvalidOperationException($"Our server did not take {Http01EndPoint}.", ex);
        }

        if (!reply.ToString().Contains($"\r\nLocation: https://x:{tlsPort}/\r\n", StringComparison.Ordinal))
            throw new InvalidOperationException($"{Http01EndPoint} answers for another server: {reply}");
    }

    public ValueTask DisposeAsync()
    {
        return _server.DisposeAsync();
    }
}
