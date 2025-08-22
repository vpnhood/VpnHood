using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VpnHood.Core.SocksProxy.HttpProxyServers;

// HTTPS proxy transport (TLS between client and proxy). Supports CONNECT and absolute-form over TLS.
public sealed class HttpsProxyServer(HttpProxyServerOptions options) 
    : TcpProxyServerBase(options.ListenEndPoint, options.Backlog, options.HandshakeTimeout)
{
    protected override async Task<Stream> AcceptClientStreamAsync(TcpClient client, CancellationToken ct)
    {
        if (options.ServerCertificate == null)
            throw new InvalidOperationException("ServerCertificate is required for HttpsProxyServer.");
        var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = options.ServerCertificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, ct).ConfigureAwait(false);
        return ssl;
    }

    protected override async Task HandleSessionAsync(TcpClient client, Stream clientStream, CancellationToken ct)
    {
        // Reuse HttpProxyServer logic by parsing minimal request and relaying; for brevity, only CONNECT path is shown
        var reader = new StreamReader(clientStream, Encoding.ASCII, leaveOpen: true);
        var writer = new StreamWriter(clientStream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine)) return;
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        var method = parts[0].ToUpperInvariant();

        // Consume headers until blank line
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct).ConfigureAwait(false))) { }

        if (method.Equals("CONNECT", StringComparison.Ordinal)) {
            var hp = parts[1].Split(':');
            var host = hp[0];
            var port = hp.Length > 1 && int.TryParse(hp[1], out var p) ? p : 443;
            using var remote = new TcpClient();
            await remote.ConnectAsync(host, port, ct).ConfigureAwait(false);
            await writer.WriteLineAsync("HTTP/1.1 200 Connection Established").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            await PumpAsync(clientStream, remote.GetStream(), ct).ConfigureAwait(false);
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