using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnHood.Test.Providers.Proxies;

// A SOCKS5 proxy, with no authentication unless given credentials, that records the destination of each connection
// asked of it, and counts the connections it takes. It relays to this machine alone, so a test of code that sends a
// check elsewhere reaches no one off it: a destination elsewhere goes to the stand-in, a server of the test's that plays
// it, or, with none, gets a success, bytes that are no TLS, and a close
public sealed class TestSocks5Proxy(IPEndPoint? standIn = null) : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private int _connectionCount;
    private int _bareCloseCount;

    public ConcurrentQueue<IPEndPoint> Destinations { get; } = new();

    // the connections it took, a dial that asks nothing too
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    // the connections closed before they asked anything, as a dial alone is
    public int BareCloseCount => Volatile.Read(ref _bareCloseCount);

    // the user name and password it asks for, or null for no authentication
    public string? Username { get; init; }
    public string? Password { get; init; }

    // how long it takes to answer each request, as a proxy far away does
    public TimeSpan ConnectDelay { get; init; }

    // what each request's answer waits for besides, or null
    public Task? ReplyGate { get; init; }

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public TestSocks5Proxy Start()
    {
        _listener.Start();
        _ = AcceptClients(_cts.Token);
        return this;
    }

    private async Task AcceptClients(CancellationToken cancellationToken)
    {
        while (true) {
            TcpClient client;
            try {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) {
                return; // the proxy's close
            }

            Interlocked.Increment(ref _connectionCount);
            _ = Serve(client, cancellationToken);
        }
    }

    private async Task Serve(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        try {
            var stream = client.GetStream();

            // the greeting: the version and the methods, answered with no authentication, or with a user name and
            // password where it asks for them, which a client that gives other ones is refused; a close before it is
            // counted
            var version = new byte[1];
            if (await stream.ReadAsync(version, cancellationToken) == 0) {
                Interlocked.Increment(ref _bareCloseCount);
                return;
            }

            var methodCount = await ReadBytes(stream, 1, cancellationToken);
            var methods = await ReadBytes(stream, methodCount.Span[0], cancellationToken);
            if (Username is null)
                await stream.WriteAsync(new byte[] { 5, 0 }, cancellationToken);
            else if (!await Authenticate(stream, methods, cancellationToken))
                return;

            // the request: the version, the command, a reserved byte, the address's type, the address and the port
            var request = await ReadBytes(stream, 4, cancellationToken);
            var addressLength = request.Span[3] switch {
                1 => 4,
                4 => 16,
                _ => throw new NotSupportedException("The test proxy takes an IP address alone.")
            };

            var addressBytes = await ReadBytes(stream, addressLength, cancellationToken);
            var port = await ReadBytes(stream, 2, cancellationToken);
            var destination = new IPEndPoint(new IPAddress(addressBytes.Span), (port.Span[0] << 8) | port.Span[1]);
            Destinations.Enqueue(destination);
            await Task.Delay(ConnectDelay, cancellationToken);
            if (ReplyGate != null)
                await ReplyGate.WaitAsync(cancellationToken);

            var hostEndPoint = IPAddress.IsLoopback(destination.Address) ? destination : standIn;
            if (hostEndPoint is null) {
                await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, cancellationToken);
                await stream.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\n"u8.ToArray(), cancellationToken);
                return;
            }

            using var host = new TcpClient(hostEndPoint.AddressFamily);
            try {
                await host.ConnectAsync(hostEndPoint, cancellationToken);
            }
            catch (SocketException) {
                await stream.WriteAsync(new byte[] { 5, 5, 0, 1, 0, 0, 0, 0, 0, 0 }, cancellationToken); // refused
                return;
            }

            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, cancellationToken);
            var hostStream = host.GetStream();
            await Task.WhenAny(
                stream.CopyToAsync(hostStream, cancellationToken),
                hostStream.CopyToAsync(stream, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or
                                       ObjectDisposedException or EndOfStreamException) {
            // the client's close or the proxy's ends a connection; the test reads what it recorded
        }
    }

    // the username and password exchange of RFC 1929: its version, then each of the two after its length
    private async Task<bool> Authenticate(Stream stream, ReadOnlyMemory<byte> methods,
        CancellationToken cancellationToken)
    {
        if (!methods.Span.Contains((byte)2)) {
            await stream.WriteAsync(new byte[] { 5, 0xFF }, cancellationToken);
            return false;
        }

        await stream.WriteAsync(new byte[] { 5, 2 }, cancellationToken);
        await ReadBytes(stream, 1, cancellationToken);
        var usernameLength = await ReadBytes(stream, 1, cancellationToken);
        var username = await ReadBytes(stream, usernameLength.Span[0], cancellationToken);
        var passwordLength = await ReadBytes(stream, 1, cancellationToken);
        var password = await ReadBytes(stream, passwordLength.Span[0], cancellationToken);
        var isValid = Encoding.UTF8.GetString(username.Span) == Username &&
                      Encoding.UTF8.GetString(password.Span) == (Password ?? "");

        await stream.WriteAsync(new byte[] { 1, (byte)(isValid ? 0 : 1) }, cancellationToken);
        return isValid;
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBytes(Stream stream, int count,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return buffer;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
