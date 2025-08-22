using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VpnHood.Core.SocksProxy.HttpsProxy;

public class HttpsProxyClient(HttpsProxyOptions options)
{
    public IPEndPoint ProxyEndPoint { get; } = options.ProxyEndPoint;
    private readonly string? _username = options.Username;
    private readonly string? _password = options.Password;
    private readonly IDictionary<string, string>? _extraHeaders = options.ExtraHeaders;
    private readonly bool _useTls = options.UseTls;
    private readonly string? _tlsHost = options.TlsHost;
    private readonly bool _allowInvalidCerts = options.AllowInvalidCertificates;

    public async Task ConnectAsync(TcpClient tcpClient, string host, int port, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));

        try {
            if (!tcpClient.Connected)
                await tcpClient.ConnectAsync(ProxyEndPoint, cancellationToken).ConfigureAwait(false);

            Stream stream = tcpClient.GetStream();

            if (_useTls) {
                var targetHost = _tlsHost ?? ProxyEndPoint.Address.ToString();
                var ssl = new SslStream(stream, leaveInnerStreamOpen: true, UserCertValidation);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                    TargetHost = targetHost,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, cancellationToken).ConfigureAwait(false);
                stream = ssl;
            }

            await SendConnectRequest(stream, host, port, cancellationToken).ConfigureAwait(false);
            await ReadConnectResponse(stream, cancellationToken).ConfigureAwait(false);
            // Do not dispose stream; caller will use tcpClient.GetStream() to continue I/O
        }
        catch {
            tcpClient.Close();
            throw;
        }
    }

    private bool UserCertValidation(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        if (_allowInvalidCerts) return true;
        return errors == SslPolicyErrors.None;
    }

    private static string BuildAuthority(string host, int port)
    {
        // IPv6 literal if it contains multiple colons
        var isIpv6Literal = host.Contains(':') && host.IndexOf(':') != host.LastIndexOf(':');
        return isIpv6Literal ? $"[{host}]:{port}" : $"{host}:{port}";
    }

    private async Task SendConnectRequest(Stream stream, string host, int port, CancellationToken cancellationToken)
    {
        var authority = BuildAuthority(host, port);

        var builder = new StringBuilder();
        builder.Append($"CONNECT {authority} HTTP/1.1\r\n");
        builder.Append($"Host: {authority}\r\n");
        builder.Append("Connection: keep-alive\r\n");
        if (_username != null) {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password ?? string.Empty}"));
            builder.Append($"Proxy-Authorization: Basic {auth}\r\n");
        }

        if (_extraHeaders != null) {
            foreach (var kv in _extraHeaders)
                builder.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
        }

        builder.Append("Content-Length: 0\r\n"); // optional, but helps with strict proxies
        builder.Append("\r\n");

        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }


    private static async Task ReadConnectResponse(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var received = 0;
        while (true) {
            var n = await stream.ReadAsync(buffer.AsMemory(received, buffer.Length - received), ct).ConfigureAwait(false);
            if (n == 0) throw new IOException( "Proxy closed connection.");
            received += n;
            if (received >= 4) {
                for (var i = 3; i < received; i++) {
                    if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n') {
                        var headerText = Encoding.UTF8.GetString(buffer, 0, i + 1);
                        ValidateStatus(headerText);
                        return;
                    }
                }
            }
            if (received == buffer.Length)
                throw new IOException("HTTP proxy response too large.");
        }
    }

    private static void ValidateStatus(string headerText)
    {
        var firstLineEnd = headerText.IndexOf("\r\n", StringComparison.Ordinal);
        if (firstLineEnd < 0) throw new IOException("Invalid HTTP proxy response.");
        var statusLine = headerText[..firstLineEnd];
        var parts = statusLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var code))
            throw new IOException("Invalid HTTP proxy status line.");

        if (code != 200)
            throw new HttpRequestException(
                message: $"HTTP proxy CONNECT failed with status {code}.", 
                inner: null,
                statusCode: (HttpStatusCode)code);
    }
}
