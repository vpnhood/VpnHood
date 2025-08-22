using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VpnHood.Core.SocksProxy.HttpsProxy;

public class HttpsProxyClient(HttpsProxyOptions options)
{
    public async Task ConnectAsync(TcpClient tcpClient, string host, int port, CancellationToken cancellationToken)
    {
        try {
            await tcpClient.ConnectAsync(options.ProxyEndPoint, cancellationToken).ConfigureAwait(false);
            Stream stream = tcpClient.GetStream();

            // If TLS is enabled, wrap the stream in an SslStream
            if (options.UseTls) {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: true, UserCertificateValidationCallback);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                    TargetHost = options.ProxyHost ?? options.ProxyEndPoint.Address.ToString(),
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, cancellationToken).ConfigureAwait(false);
                stream = ssl;
            }

            // Send the CONNECT request to the proxy
            await SendConnectRequest(stream, host, port, cancellationToken).ConfigureAwait(false);
            await ReadConnectResponse(stream, cancellationToken).ConfigureAwait(false);
        }
        catch {
            tcpClient.Close();
            throw;
        }
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        return options.AllowInvalidCertificates || sslPolicyErrors == SslPolicyErrors.None;
    }

    private static string BuildAuthority(string host, int port)
    {
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
        if (options.Username != null) {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password ?? string.Empty}"));
            builder.Append($"Proxy-Authorization: Basic {auth}\r\n");
        }
        if (options.ExtraHeaders != null) {
            foreach (var kv in options.ExtraHeaders)
                builder.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
        }
        builder.Append("Content-Length: 0\r\n");
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
            if (n == 0) throw new IOException("Proxy closed connection.");
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
            throw new HttpRequestException(message: $"HTTP proxy CONNECT failed with status {code}.", inner: null, statusCode: (HttpStatusCode)code);
    }
}
