using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.SocksProxy.HttpProxyServers;

public abstract class TcpProxyServerBase
{
    protected readonly IPEndPoint ListenEndPoint;
    protected readonly int Backlog;
    protected readonly TimeSpan HandshakeTimeout;
    protected readonly TcpListener Listener;

    protected TcpProxyServerBase(IPEndPoint listenEndPoint, int backlog, TimeSpan handshakeTimeout)
    {
        ListenEndPoint = listenEndPoint;
        Backlog = backlog;
        HandshakeTimeout = handshakeTimeout;
        Listener = new TcpListener(ListenEndPoint);
    }

    public void Start() => Listener.Start(Backlog);
    public void Stop() => Listener.Stop();

    public async Task RunAsync(CancellationToken ct)
    {
        Start();
        try {
            while (!ct.IsCancellationRequested) {
                var client = await Listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
        }
        finally { Stop(); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCt)
    {
        using var tcp = client;
        tcp.NoDelay = true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        cts.CancelAfter(HandshakeTimeout);
        var ct = cts.Token;

        try {
            using var clientStream = await AcceptClientStreamAsync(tcp, ct).ConfigureAwait(false);
            await HandleSessionAsync(tcp, clientStream, serverCt).ConfigureAwait(false);
        }
        catch { /* keep server running */ }
    }

    protected abstract Task<Stream> AcceptClientStreamAsync(TcpClient client, CancellationToken ct);
    protected abstract Task HandleSessionAsync(TcpClient client, Stream clientStream, CancellationToken ct);
}