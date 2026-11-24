using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace VpnHood.Test.Providers.Proxies;

// A proxy that takes each connection and answers nothing, as one that hangs. It holds them for the test, which can
// close one to fail its dial at once
internal sealed class SilentProxy : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<TcpClient> _accepted = Channel.CreateUnbounded<TcpClient>();
    private readonly ConcurrentQueue<TcpClient> _connections = new();
    private Task _accepting = Task.CompletedTask;

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;
    public int ConnectionCount => _connections.Count;

    // the connections it took that sent something, as a TLS hello, which it never reads
    public int HelloCount => _connections.Count(HasData);

    public SilentProxy Start()
    {
        _listener.Start();
        _accepting = Accept(_cts.Token);
        return this;
    }

    private async Task Accept(CancellationToken cancellationToken)
    {
        try {
            while (true) {
                var connection = await _listener.AcceptTcpClientAsync(cancellationToken);
                _connections.Enqueue(connection);
                _accepted.Writer.TryWrite(connection);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) {
            // the proxy's close
        }
    }

    private static bool HasData(TcpClient connection)
    {
        try {
            return connection.Available > 0;
        }
        catch (ObjectDisposedException) {
            // one the test closed
            return false;
        }
    }

    // the connections it took, each once, in turn
    public ValueTask<TcpClient> TakeConnection(CancellationToken cancellationToken) =>
        _accepted.Reader.ReadAsync(cancellationToken);

    // the connections that sent something closed, so the checks that hold them fail at once
    public void CloseHelloConnections()
    {
        foreach (var connection in _connections.Where(HasData).ToArray())
            connection.Dispose();
    }

    // after its accept loop's end, which its token ends, so no connection it takes at the close is left open
    public void Dispose()
    {
        _cts.Cancel();
        _accepting.GetAwaiter().GetResult();
        _listener.Stop();
        _accepted.Writer.TryComplete();
        foreach (var connection in _connections)
            connection.Dispose();

        _cts.Dispose();
    }
}
