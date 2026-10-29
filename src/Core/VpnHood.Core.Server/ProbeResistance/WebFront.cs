using System.Net.Sockets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.ProbeResistance;

// the server's HTTP face to anyone who is not a client: ASP.NET Core's Kestrel behind a JWT bearer check, and HTTPS
// redirection on port 80, with Kestrel's parsing, limits, timeouts and keep-alive. Recognized client requests switch
// to their protocol after the headers; tests/VpnHood.Test.KestrelParity compares ordinary requests with a real
// Kestrel's answers, probe by probe
internal static class WebFront
{
    // Kestrel's defaults; its TLS handshake has its own timeout, which no heartbeat rounds
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(130);
    private static readonly TimeSpan RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestBodyDrainTimeout = TimeSpan.FromSeconds(5);
    private const long MaxRequestBodySize = 30_000_000;

    // the next request; null once the connection is to close: it ended, sat idle, or sent a request Kestrel refuses,
    // which this has answered
    public static async Task<HttpRequestHead?> ReadRequest(HttpReadStream stream, CancellationToken cancellationToken)
    {
        if (!await FindRequestStart(stream, cancellationToken).Vhc())
            return null;

        using var headersCts = new CancellationTokenSource(Heartbeat(RequestHeadersTimeout));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(headersCts.Token, cancellationToken);
        try {
            return await HttpRequestHeadReader.Read(stream, linkedCts.Token).Vhc();
        }
        catch (HttpRequestRejectedException ex) {
            await TryWrite(stream, WebReplies.Rejected(ex.StatusCode, ex.Allow), cancellationToken).Vhc();
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException &&
                                   headersCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            await TryWrite(stream, WebReplies.Rejected(408, null), cancellationToken).Vhc();
            return null;
        }
        catch (EndOfStreamException) {
            return null;
        }
    }

    // 401, as a JWT bearer check answers a request without a valid token
    public static Task<WebFrontNext> Unauthorized(HttpReadStream stream, HttpRequestHead request,
        CancellationToken cancellationToken)
    {
        return Answer(stream, request, WebReplies.Unauthorized(request), cancellationToken);
    }

    // 307 to the same URL on HTTPS, as HTTPS redirection answers on port 80
    public static Task<WebFrontNext> RedirectToHttps(HttpReadStream stream, HttpRequestHead request, int httpsPort,
        CancellationToken cancellationToken)
    {
        return Answer(stream, request, WebReplies.RedirectToHttps(request, httpsPort), cancellationToken);
    }

    private static async Task<WebFrontNext> Answer(HttpReadStream stream, HttpRequestHead request, byte[] reply,
        CancellationToken cancellationToken)
    {
        if (!await TryWrite(stream, reply, cancellationToken).Vhc())
            return WebFrontNext.Reset;
        if (!request.KeepAlive || request.ContentLength > MaxRequestBodySize)
            return WebFrontNext.Close;

        // the body the reply didn't need is read and dropped before the next request, within Kestrel's drain
        // timeout, or the connection resets; one cut short or malformed ends it
        using var drainCts = new CancellationTokenSource(Heartbeat(RequestBodyDrainTimeout));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(drainCts.Token, cancellationToken);
        try {
            return await DrainBody(stream, request, linkedCts.Token).Vhc()
                ? WebFrontNext.NextRequest
                : WebFrontNext.Reset;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException &&
                                   drainCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            return WebFrontNext.Reset;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or HttpRequestRejectedException) {
            return WebFrontNext.Close;
        }
    }

    private static async Task<bool> DrainBody(HttpReadStream stream, HttpRequestHead request,
        CancellationToken cancellationToken)
    {
        if (request.IsChunked) {
            var body = new HttpChunkedBody(MaxRequestBodySize);
            return await body.Drain(stream, request, cancellationToken).Vhc();
        }

        var remaining = request.ContentLength ?? 0;
        while (remaining > 0) {
            if (!await stream.EnsureBuffered(1, cancellationToken).Vhc())
                throw new EndOfStreamException();
            var count = (int)Math.Min(remaining, stream.BufferedSpan.Length);
            stream.Consume(count);
            remaining -= count;
        }
        return true;
    }

    // Leading line ends do not start the headers deadline. Skip whole buffered spans under the original
    // keep-alive deadline; leave the first other byte for the request reader.
    private static async Task<bool> FindRequestStart(HttpReadStream stream, CancellationToken cancellationToken)
    {
        using var idleCts = new CancellationTokenSource(Heartbeat(KeepAliveTimeout));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(idleCts.Token, cancellationToken);
        try {
            while (await stream.EnsureBuffered(1, linkedCts.Token).Vhc()) {
                var span = stream.BufferedSpan;
                var start = span.IndexOfAnyExcept((byte)'\r', (byte)'\n');
                stream.Consume(start < 0 ? span.Length : start);
                if (start >= 0)
                    return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException &&
                                   idleCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            return false;
        }
    }

    // Kestrel checks a timeout on its one-second heartbeat, a beat after it is due: so it fires between one and two
    // seconds late
    private static TimeSpan Heartbeat(TimeSpan timeout)
    {
        var now = Environment.TickCount64;
        var at = (now + (long)timeout.TotalMilliseconds + 1000 + 999) / 1000 * 1000;
        return TimeSpan.FromMilliseconds(at - now);
    }

    // A stalled reader must not hold a connection indefinitely. Kestrel allows five seconds of grace and
    // 240 bytes per second for its response; its heartbeat bounds the wait for each reply here.
    public static async Task<bool> TryWrite(Stream stream, ReadOnlyMemory<byte> reply, CancellationToken cancellationToken)
    {
        using var writeCts = new CancellationTokenSource(Heartbeat(TimeSpan.FromSeconds(5 + reply.Length / 240.0)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(writeCts.Token, cancellationToken);
        try {
            await stream.WriteAsync(reply, linkedCts.Token).Vhc();
            await stream.FlushAsync(linkedCts.Token).Vhc();
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException &&
                                   writeCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            return false;
        }
        catch (Exception ex) when (IsDisconnect(ex)) {
            return false;
        }
    }

    public static bool IsDisconnect(Exception exception) => exception switch {
        ObjectDisposedException or EndOfStreamException => true,
        SocketException ex => ex.SocketErrorCode is SocketError.ConnectionAborted or SocketError.ConnectionReset or
            SocketError.Shutdown or SocketError.NotConnected or SocketError.OperationAborted,
        IOException { InnerException: { } inner } => IsDisconnect(inner),
        _ => false
    };
}
