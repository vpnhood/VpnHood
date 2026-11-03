using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.AccessManagers;

// An access manager on both loopback addresses at one port, which answers a server's configure and status
// calls and records the address family each call came over: TestHttpAccessManagerServer listens on IPv4
// only, so it cannot tell.
public sealed class TestDualStackAccessManagerServer : IDisposable
{
    private const string ConfigureReply = "{}";
    private const string StatusReply = """{"configCode":"test"}""";
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IReadOnlyList<TcpListener> _listeners;
    private readonly ConcurrentQueue<(string Path, AddressFamily AddressFamily)> _calls = new();

    public Uri BaseUri { get; }

    public IReadOnlyList<AddressFamily> GetCallFamilies(string route) => _calls
        .Where(x => x.Path.EndsWith("/" + route, StringComparison.Ordinal))
        .Select(x => x.AddressFamily)
        .ToArray();

    public TestDualStackAccessManagerServer(bool listenIpV6)
    {
        _listeners = StartListeners(listenIpV6);
        BaseUri = new Uri($"http://localhost:{((IPEndPoint)_listeners[0].LocalEndpoint).Port}");
        foreach (var listener in _listeners)
            _ = Accept(listener, _cancellationTokenSource.Token);
    }

    private static IReadOnlyList<TcpListener> StartListeners(bool listenIpV6)
    {
        while (true) {
            var port = TestWebServerLocalEps.AllocateFreeTcpEndPoint(IPAddress.Loopback).Port;
            var listenerV4 = new TcpListener(IPAddress.Loopback, port);
            listenerV4.Start();
            if (!listenIpV6)
                return [listenerV4];

            var listenerV6 = new TcpListener(IPAddress.IPv6Loopback, port);
            try {
                listenerV6.Start();
                return [listenerV4, listenerV6];
            }
            catch (SocketException) {
                // the port is taken on IPv6: another one
                listenerV4.Stop();
                listenerV6.Dispose();
            }
        }
    }

    private async Task Accept(TcpListener listener, CancellationToken cancellationToken)
    {
        try {
            while (true) {
                var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Serve(tcpClient, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // disposed
        }
    }

    private async Task Serve(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var client = tcpClient;
        await using var stream = tcpClient.GetStream();
        var path = await ReadRequestPath(stream, cancellationToken);
        _calls.Enqueue((path, tcpClient.Client.GetRemoteEndPoint().AddressFamily));

        var body = path.EndsWith("/status", StringComparison.Ordinal) ? StatusReply : ConfigureReply;
        var response = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                       $"Content-Length: {Encoding.ASCII.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
    }

    // the whole request is read before the reply, as closing on unread data resets the connection
    private static async Task<string> ReadRequestPath(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken) ?? throw new EndOfStreamException();
        var contentLength = await ReadContentLength(reader, cancellationToken);
        await reader.ReadBlockAsync(new char[contentLength], cancellationToken);
        return requestLine.Split(' ')[1];
    }

    // the headers end at a blank line; a status's JSON is ASCII, so its length in bytes is its length in chars
    private static async Task<int> ReadContentLength(StreamReader reader, CancellationToken cancellationToken)
    {
        const string contentLengthHeader = "Content-Length:";
        var contentLength = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
            if (line.StartsWith(contentLengthHeader, StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line[contentLengthHeader.Length..].Trim());

        return contentLength;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        foreach (var listener in _listeners)
            listener.Stop();

        _cancellationTokenSource.Dispose();
    }
}
