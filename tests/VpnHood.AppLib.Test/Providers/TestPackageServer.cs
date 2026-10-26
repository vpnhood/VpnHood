using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.AppLib.Test.Daemon;

namespace VpnHood.AppLib.Test.Providers;

// A release's package on a loopback server, for a desktop's download: a range answered as a CDN answers it, unless
// told to ignore ranges, and a response cut after the bytes given, as a dropped connection cuts it.
internal sealed class TestPackageServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly byte[] _package;

    public TestPackageServer(byte[] package)
    {
        _package = package;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = Serve(_cancellation.Token);
    }

    public Uri PackageUrl => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/Package.bin");

    // each request's Range header, null for one without
    public ConcurrentQueue<string?> Ranges { get; } = new();

    // the next response's body ends after these bytes
    public int? CutAfterBytes { get; set; }

    public bool IsRangeIgnored { get; set; }

    private async Task Serve(CancellationToken cancellationToken)
    {
        try {
            while (true) {
                using var socket = await _listener.AcceptSocketAsync(cancellationToken);
                await using var stream = new NetworkStream(socket, ownsSocket: false);
                var request = await TestHttpServer.ReadRequest(stream, cancellationToken);
                if (request == null)
                    continue;

                var range = request.Headers.GetValueOrDefault("Range");
                Ranges.Enqueue(range);
                var from = range != null && !IsRangeIgnored ? int.Parse(range["bytes=".Length..].TrimEnd('-')) : 0;
                var head = from >= _package.Length
                    ? $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{_package.Length}\r\nContent-Length: 0\r\n"
                    : from > 0
                        ? $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {from}-{_package.Length - 1}/{_package.Length}\r\n" +
                          $"Content-Length: {_package.Length - from}\r\n"
                        : $"HTTP/1.1 200 OK\r\nContent-Length: {_package.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head + "Accept-Ranges: bytes\r\nConnection: close\r\n\r\n"),
                    cancellationToken);

                var count = Math.Max(_package.Length - from, 0);
                if (CutAfterBytes is { } cutAfterBytes) {
                    count = Math.Min(count, cutAfterBytes);
                    CutAfterBytes = null;
                }

                await stream.WriteAsync(_package.AsMemory(Math.Min(from, _package.Length), count), cancellationToken);
                socket.Shutdown(SocketShutdown.Both);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) {
            // stopped
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.Stop();
        _cancellation.Dispose();
    }
}
