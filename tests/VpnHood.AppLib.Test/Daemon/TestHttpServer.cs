using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnHood.AppLib.Test.Daemon;

// The daemon's API, stood in for by the least HTTP server that records what reached it. Each
// connection is closed after one request, so the connection check runs for every call.
internal sealed class TestHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();

    public TestHttpServer(Func<TestHttpRequest, (HttpStatusCode Status, string Body)> answer)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = Serve(answer, _cancellation.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public ConcurrentQueue<TestHttpRequest> Requests { get; } = new();

    private async Task Serve(Func<TestHttpRequest, (HttpStatusCode Status, string Body)> answer, CancellationToken cancellationToken)
    {
        try {
            while (true) {
                using var socket = await _listener.AcceptSocketAsync(cancellationToken);
                await using var stream = new NetworkStream(socket, ownsSocket: false);
                var request = await ReadRequest(stream, cancellationToken);
                if (request == null)
                    continue; // connected and said nothing: a connection the check refused

                Requests.Enqueue(request);
                var (status, body) = answer(request);
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var head = $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: text/plain\r\n" +
                           $"Content-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken);
                await stream.WriteAsync(bodyBytes, cancellationToken);
                socket.Shutdown(SocketShutdown.Both);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) {
            // stopped
        }
    }

    // The request line and the headers; a body, when there is one, is read and dropped.
    private static async Task<TestHttpRequest?> ReadRequest(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var length = 0;
        int headEnd;
        while (true) {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
                return null;

            length += read;
            headEnd = Encoding.ASCII.GetString(buffer, 0, length).IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headEnd >= 0)
                break;
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headEnd).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var headers = lines.Skip(1)
            .Select(line => line.Split(':', 2))
            .ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.OrdinalIgnoreCase);

        if (headers.TryGetValue("Content-Length", out var contentLength) && int.TryParse(contentLength, out var bodyLength)) {
            var bodyRead = length - (headEnd + 4);
            while (bodyRead < bodyLength) {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                bodyRead += read;
            }
        }

        return new TestHttpRequest(requestLine[0], requestLine[1], headers);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.Stop();
        _cancellation.Dispose();
    }
}
