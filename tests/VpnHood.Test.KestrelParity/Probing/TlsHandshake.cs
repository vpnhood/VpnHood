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
    // a capture is shared by both servers' runs, so it ends on its own rather than with either run
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    // A handshake that fails before its hello, as one asking for a TLS version this machine lacks, ends the capture
    // with its error rather than leaving it waiting. Resumption is off, so the hello can't offer a session another
    // probe's connection left in this process's cache, which one server would resume and the other not.
    public static async Task<byte[]> CaptureClientHello(string? sni, IReadOnlyList<SslApplicationProtocol>? alpn,
        SslProtocols protocols, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CaptureTimeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var acceptTask = listener.AcceptTcpClientAsync(timeoutCts.Token);
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeoutCts.Token);
        using var server = await acceptTask;
        await using var sslStream = new SslStream(client.GetStream());
        var authTask = sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = sni ?? "",
            ApplicationProtocols = alpn?.ToList(),
            EnabledSslProtocols = protocols,
            AllowTlsResume = false,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }, timeoutCts.Token);

        // nothing answers the handshake, so it ends first only by failing
        var readTask = ReadRecord(server.GetStream(), timeoutCts.Token);
        if (await Task.WhenAny(readTask, authTask) == authTask)
            await authTask;
        var hello = await readTask;
        server.Close();
        try {
            await authTask;
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException) {
            // the listener closes once it has the hello, so the handshake can only fail
        }

        return hello;
    }

    private static async Task<byte[]> ReadRecord(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[5];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var body = new byte[(header[3] << 8) | header[4]];
        await stream.ReadExactlyAsync(body, cancellationToken);
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

            // a record cut short says how much of it came, so a truncated answer can't pass for a whole one
            if (body.Length < length)
                parts[^1] += $"[{body.Length} of {length}]";
        }

        // what is too short for a record's header: the whole answer, or what came after its last record
        if (i < data.Length)
            parts.Add(parts.Count == 0
                ? $"raw({data.Length}): {ProbeConnection.Escape(Encoding.Latin1.GetString(data))}"
                : $"+{data.Length - i} bytes");

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
            // a message cut short is only counted, as a ServerHello is read field by field
            parts.Add(msg.Length < length
                ? $"Msg{msgType}({length})[{msg.Length} of {length}]"
                : msgType switch {
                    2 => DescribeServerHello(msg),
                    4 => $"NewSessionTicket({length})",
                    11 => $"Certificate({length})",
                    12 => $"ServerKeyExchange({length})",
                    13 => $"CertificateRequest({length})",
                    14 => "ServerHelloDone",
                    _ => $"Msg{msgType}({length})"
                });
        }

        // too few for a message's header after the last message, which the record's own length doesn't show
        if (i < body.Length)
            parts.Add($"+{body.Length - i} bytes");

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
