using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnHood.Core.SocksProxy.HttpProxyServers;

// Plain HTTP proxy server supporting absolute-form GET and CONNECT; no TLS on proxy transport
public sealed class HttpProxyServer(HttpProxyServerOptions options) : TcpProxyServerBase(options.ListenEndPoint, options.Backlog, options.HandshakeTimeout)
{
    protected override Task<Stream> AcceptClientStreamAsync(TcpClient client, CancellationToken ct)
        => Task.FromResult<Stream>(client.GetStream());

    protected override async Task HandleSessionAsync(TcpClient client, Stream clientStream, CancellationToken ct)
    {
        var stream = clientStream;
        var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        // Read request line
        var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine)) return;
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        var method = parts[0].ToUpperInvariant();

        // Read headers
        string? hostHeader = null;
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct).ConfigureAwait(false))) {
            var idx = line.IndexOf(':');
            if (idx > 0) {
                var name = line.Substring(0, idx).Trim();
                var value = line.Substring(idx + 1).Trim();
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) hostHeader = value;
            }
        }

        if (method.Equals("CONNECT", StringComparison.Ordinal)) {
            // Format: CONNECT host:port HTTP/1.1
            var authority = parts[1];
            var hp = authority.Split(':');
            var host = hp[0];
            var port = hp.Length > 1 && int.TryParse(hp[1], out var p) ? p : 443;

            using var remote = new TcpClient();
            await remote.ConnectAsync(host, port, ct).ConfigureAwait(false);
            await writer.WriteLineAsync("HTTP/1.1 200 Connection Established").ConfigureAwait(false);
            await writer.WriteLineAsync("Proxy-Connection: keep-alive").ConfigureAwait(false);
            await writer.WriteLineAsync("Connection: keep-alive").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);

            await PumpAsync(stream, remote.GetStream(), ct).ConfigureAwait(false);
            return;
        }
        else {
            // Absolute-form request expected: GET http://host/path HTTP/1.1
            var uri = new Uri(parts[1], UriKind.Absolute);
            var host = string.IsNullOrEmpty(uri.Host) ? hostHeader ?? throw new InvalidOperationException("Host header required.") : uri.Host;
            var port = uri.IsDefaultPort ? (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80) : uri.Port;

            using var remote = new TcpClient();
            await remote.ConnectAsync(host, port, ct).ConfigureAwait(false);
            var remoteStream = remote.GetStream();
            var reqBuilder = new StringBuilder();
            // Rewrite request line to origin-form
            reqBuilder.Append(method).Append(' ').Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
            // Forward headers; ensure Host present
            if (hostHeader == null) reqBuilder.Append("Host: ").Append(host).Append(":").Append(port).Append("\r\n");
            // For simplicity, we do not forward other headers read above; in production, buffer and forward all
            reqBuilder.Append("Connection: close\r\n\r\n");
            var bytes = Encoding.ASCII.GetBytes(reqBuilder.ToString());
            await remoteStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await remoteStream.FlushAsync(ct).ConfigureAwait(false);
            await PumpAsync(remoteStream, stream, ct).ConfigureAwait(false);
            return;
        }
    }

    private static async Task PumpAsync(Stream a, Stream b, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var t1 = a.CopyToAsync(b, ct);
        var t2 = b.CopyToAsync(a, ct);
        await Task.WhenAny(t1, t2).ConfigureAwait(false);
        cts.Cancel();
    }
}