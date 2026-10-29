using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Test.KestrelParity.Probing;

// One probe's connection: what was sent, what came back, and how and when the peer ended it. The log holds only
// what a prober could compare: a reply's Date is masked, and times are whole seconds since the last send.
internal sealed partial class ProbeConnection : IAsyncDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly Stream _stream;
    private readonly TapStream? _tap;
    private readonly Stopwatch _sinceSend = Stopwatch.StartNew();
    private readonly StringBuilder _log;
    private readonly MemoryStream _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _readCts = new();
    private string? _endState;
    private TimeSpan _endTime;
    private bool _endLogged;

    private ProbeConnection(TcpClient tcpClient, Stream stream, TapStream? tap, StringBuilder log)
    {
        _tcpClient = tcpClient;
        _stream = stream;
        _tap = tap;
        _log = log;
        _ = ReadLoop(_readCts.Token);
    }

    public static async Task<ProbeConnection> Open(IPEndPoint endPoint, bool tls, string? sni, StringBuilder log,
        CancellationToken cancellationToken, IReadOnlyList<SslApplicationProtocol>? alpn = null,
        SslProtocols sslProtocols = SslProtocols.None, bool bufferWrites = false)
    {
        var tcpClient = new TcpClient(endPoint.AddressFamily) { NoDelay = true };
        if (bufferWrites)
            tcpClient.SendBufferSize = 1024 * 1024;
        await tcpClient.ConnectAsync(endPoint, cancellationToken);
        if (!tls)
            return new ProbeConnection(tcpClient, tcpClient.GetStream(), null, log);

        var tap = new TapStream(tcpClient.GetStream());
        var sslStream = new SslStream(tap, false);
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = sni ?? "",
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
            ApplicationProtocols = alpn?.ToList(),
            EnabledSslProtocols = sslProtocols
        }, cancellationToken);
        tap.MarkHandshakeDone();
        if (bufferWrites)
            tap.BufferWrites();
        return new ProbeConnection(tcpClient, sslStream, tap, log);
    }

    private async Task ReadLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try {
            while (true) {
                var read = await _stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) {
                    SetEnd("closed");
                    return;
                }

                lock (_pending) _pending.Write(buffer, 0, read);
                _signal.Release();
            }
        }
        catch (Exception ex) {
            SetEnd(IsReset(ex) ? "reset" : $"error {Describe(ex)}");
        }
    }

    private void SetEnd(string state)
    {
        lock (_pending) {
            _endTime = _sinceSend.Elapsed;
            _endState = state;
        }
        _signal.Release();
    }

    // Latin-1, so a "é" in the text goes as the byte 0xE9
    public Task Send(string text, CancellationToken cancellationToken)
    {
        return Send(Encoding.Latin1.GetBytes(text), Escape(text), cancellationToken);
    }

    public async Task Send(byte[] data, string label, CancellationToken cancellationToken)
    {
        _log.AppendLine($"> {label}");
        _sinceSend.Restart();
        try {
            await _stream.WriteAsync(data, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
            if (_tap != null)
                await _tap.FlushBufferedWrites(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            // a server that has already ended the connection is what the log records, not a failed probe
            _log.AppendLine($"  (send failed: {Describe(ex)})");
        }
    }

    // takes what comes until the peer ends the connection, nothing more comes for `quiet` after the first bytes,
    // or `max` passes
    public async Task Read(TimeSpan quiet, TimeSpan max, CancellationToken cancellationToken)
    {
        var received = new MemoryStream();
        var clock = Stopwatch.StartNew();
        while (!_endLogged) {
            if (TakePending(received) is { } endState) {
                Flush(received);
                LogEnd(endState);
                return;
            }

            var wait = received.Length > 0 ? quiet : max - clock.Elapsed;
            if (wait <= TimeSpan.Zero || !await _signal.WaitAsync(wait, cancellationToken)) {
                TakePending(received);
                break;
            }
        }

        Flush(received);
    }

    // waits for the peer to end the connection, up to `max`
    public async Task WaitEnd(TimeSpan max, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        while (!_endLogged && clock.Elapsed < max)
            await Read(max - clock.Elapsed, max - clock.Elapsed, cancellationToken);

        if (!_endLogged)
            _log.AppendLine($"= open after {Seconds(_sinceSend.Elapsed)}s");
    }

    private string? TakePending(MemoryStream received)
    {
        lock (_pending) {
            if (_pending.Length > 0) {
                _pending.Position = 0;
                _pending.CopyTo(received);
                _pending.SetLength(0);
            }
            return _endState;
        }
    }

    private void Flush(MemoryStream received)
    {
        if (received.Length == 0) return;
        var text = Encoding.Latin1.GetString(received.ToArray());
        _log.AppendLine($"< {Escape(DateRegex().Replace(text, "Date: *"))}");
        received.SetLength(0);
    }

    private void LogEnd(string how)
    {
        _endLogged = true;
        var tail = _tap?.TailRecords() is { Length: > 0 } records ? $" tls-tail[{records}]" : "";
        _log.AppendLine($"= {how} after {Seconds(_endTime)}s{tail}");
    }

    private static string Seconds(TimeSpan elapsed)
    {
        return elapsed.TotalSeconds < 0.5 ? "0" : Math.Round(elapsed.TotalSeconds).ToString("0");
    }

    private static bool IsReset(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
                return true;
        return false;
    }

    public static string Describe(Exception ex)
    {
        var inner = ex.InnerException != null ? $" | {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
        return $"{ex.GetType().Name}: {ex.Message}{inner}";
    }

    public static string Escape(string text)
    {
        var escaped = text.Replace("\r", "\\r").Replace("\n", "\\n");
        return escaped.Length > 400 ? escaped[..400] + $"...({text.Length} chars)" : escaped;
    }

    [GeneratedRegex(@"Date: [^\r\n]*")]
    private static partial Regex DateRegex();

    public async ValueTask DisposeAsync()
    {
        await _readCts.CancelAsync();
        await _stream.TryDisposeAsync();
        _tcpClient.Dispose();
        _readCts.Dispose();
    }
}
