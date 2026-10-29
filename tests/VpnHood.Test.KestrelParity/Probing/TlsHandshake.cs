using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace VpnHood.Test.KestrelParity.Probing;

// The handshake as a prober sees it: one fixed ClientHello replayed to each server, and its answer described by
// its structure (versions, cipher, extensions, record sizes), with the random values left out.
internal static class TlsHandshake
{
    public static async Task<byte[]> CaptureClientHello(string? sni, IReadOnlyList<SslApplicationProtocol>? alpn,
        SslProtocols protocols, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var acceptTask = listener.AcceptTcpClientAsync(cancellationToken);
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, cancellationToken);
        using var server = await acceptTask;
        await using var sslStream = new SslStream(client.GetStream());
        var authTask = sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = sni ?? "",
            ApplicationProtocols = alpn?.ToList(),
            EnabledSslProtocols = protocols,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }, cancellationToken);

        var stream = server.GetStream();
        var header = new byte[5];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var body = new byte[(header[3] << 8) | header[4]];
        await stream.ReadExactlyAsync(body, cancellationToken);
        server.Close();
        try {
            await authTask;
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException) {
            // the listener closes once it has the hello, so the handshake can only fail
        }

        return [.. header, .. body];
    }

    public static async Task<string> Replay(IPEndPoint endPoint, byte[] firstFlight, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient(endPoint.AddressFamily);
        await tcpClient.ConnectAsync(endPoint, cancellationToken);
        var stream = tcpClient.GetStream();
        await stream.WriteAsync(firstFlight, cancellationToken);

        var data = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var clock = Stopwatch.StartNew();
        var end = "open";
        while (clock.Elapsed < TimeSpan.FromSeconds(3)) {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(data.Length > 0 ? TimeSpan.FromMilliseconds(800) : TimeSpan.FromSeconds(3));
            try {
                var read = await stream.ReadAsync(buffer, readCts.Token);
                if (read == 0) {
                    end = $"closed after {Math.Round(clock.Elapsed.TotalSeconds)}s";
                    break;
                }
                data.Write(buffer, 0, read);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                break;
            }
            catch (IOException ex) {
                end = ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset }
                    ? "reset"
                    : ProbeConnection.Describe(ex);
                break;
            }
        }

        return $"{Summarize(data.ToArray())} | {end}";
    }

    private static string Summarize(byte[] data)
    {
        var parts = new List<string>();
        var i = 0;
        var encrypted = false;
        while (i + 5 <= data.Length) {
            var type = data[i];
            var version = (data[i + 1] << 8) | data[i + 2];
            var length = (data[i + 3] << 8) | data[i + 4];
            var body = data.AsSpan(i + 5, Math.Min(length, data.Length - i - 5));
            i += 5 + length;
            switch (type) {
                case 22 when !encrypted:
                    parts.Add($"HS[v{version:x4}]({DescribeHandshake(body)})");
                    break;
                case 20:
                    parts.Add("CCS");
                    break;
                case 21 when body.Length == 2:
                    parts.Add($"Alert({body[0]},{body[1]})");
                    break;
                case 23:
                    encrypted = true;
                    parts.Add($"App({length})");
                    break;
                default:
                    parts.Add($"Rec{type}({length})");
                    break;
            }
        }

        if (i < data.Length && parts.Count == 0)
            parts.Add($"raw({data.Length}): {ProbeConnection.Escape(Encoding.Latin1.GetString(data))}");

        return parts.Count == 0 ? "nothing" : string.Join(" ", parts);
    }

    private static string DescribeHandshake(ReadOnlySpan<byte> body)
    {
        var parts = new List<string>();
        var i = 0;
        while (i + 4 <= body.Length) {
            var msgType = body[i];
            var length = (body[i + 1] << 16) | (body[i + 2] << 8) | body[i + 3];
            var msg = body.Slice(i + 4, Math.Min(length, body.Length - i - 4));
            i += 4 + length;
            parts.Add(msgType switch {
                2 => DescribeServerHello(msg),
                4 => $"NewSessionTicket({length})",
                11 => $"Certificate({length})",
                12 => $"ServerKeyExchange({length})",
                13 => $"CertificateRequest({length})",
                14 => "ServerHelloDone",
                _ => $"Msg{msgType}({length})"
            });
        }

        return string.Join(" ", parts);
    }

    private static string DescribeServerHello(ReadOnlySpan<byte> msg)
    {
        var version = (msg[0] << 8) | msg[1];
        var p = 2 + 32;
        var sessionIdLength = msg[p];
        p += 1 + sessionIdLength;
        var cipher = (msg[p] << 8) | msg[p + 1];
        p += 2;
        var compression = msg[p];
        p += 1;
        var extensions = new List<string>();
        if (p + 2 <= msg.Length) {
            var extLength = (msg[p] << 8) | msg[p + 1];
            p += 2;
            var end = Math.Min(msg.Length, p + extLength);
            while (p + 4 <= end) {
                var extType = (msg[p] << 8) | msg[p + 1];
                var len = (msg[p + 2] << 8) | msg[p + 3];
                var extData = msg.Slice(p + 4, Math.Min(len, end - p - 4));
                p += 4 + len;
                // the key share's own bytes change on every handshake; its group does not
                var shown = extType == 0x0033 ? extData[..Math.Min(2, extData.Length)] : extData;
                extensions.Add($"{extType:x4}:{len}={Convert.ToHexString(shown)}");
            }
        }

        return $"ServerHello(v{version:x4} sid{sessionIdLength} cs{cipher:x4} comp{compression} ext[{string.Join(",", extensions)}])";
    }
}
