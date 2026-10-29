using System.Net.Sockets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.ProbeResistance;

// the server's HTTP face to anyone who is not a client: ASP.NET Core's Kestrel behind a JWT bearer check, and HTTPS
// redirection on port 80, with Kestrel's parsing, limits, timeouts and keep-alive. Recognized client requests switch
// to their protocol after the headers; the parity harness of the IP-blocking plan compares ordinary requests
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
    public static async Task<HttpRequestHead?> ReadRequest(Stream stream, CancellationToken cancellationToken)
    {
        var firstByte = await ReadFirstByte(stream, cancellationToken).Vhc();
        if (firstByte < 0)
            return null;

        using var headersCts = new CancellationTokenSource(Heartbeat(RequestHeadersTimeout));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(headersCts.Token, cancellationToken);
        try {
            return await HttpRequestHeadReader.Read(stream, (byte)firstByte, linkedCts.Token).Vhc();
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
    public static Task<WebFrontNext> Unauthorized(Stream stream, HttpRequestHead request,
        CancellationToken cancellationToken)
    {
        return Answer(stream, request, WebReplies.Unauthorized(request), cancellationToken);
    }

    // 307 to the same URL on HTTPS, as HTTPS redirection answers on port 80
    public static Task<WebFrontNext> RedirectToHttps(Stream stream, HttpRequestHead request, int httpsPort,
        CancellationToken cancellationToken)
    {
        return Answer(stream, request, WebReplies.RedirectToHttps(request, httpsPort), cancellationToken);
    }

    private static async Task<WebFrontNext> Answer(Stream stream, HttpRequestHead request, byte[] reply,
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
                : WebFrontNext.Close;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException &&
                                   drainCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            return WebFrontNext.Reset;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or HttpRequestRejectedException) {
            return WebFrontNext.Close;
        }
    }

    private static async Task<bool> DrainBody(Stream stream, HttpRequestHead request,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        if (!request.IsChunked) {
            await Skip(stream, buffer, request.ContentLength ?? 0, cancellationToken).Vhc();
            return true;
        }

        // chunks: a hex size and maybe extensions, the data, a line end; then trailers to an empty line
        long total = 0;
        while (true) {
            var size = await ReadChunkSize(stream, buffer, cancellationToken).Vhc();

            if (size == 0)
                break;

            // Kestrel applies the limit to bytes received, so an oversized declaration with no data still
            // waits for the drain deadline. Counting the declaration would close it immediately instead.
            while (size > 0) {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(size, buffer.Length)),
                    cancellationToken).Vhc();
                if (read == 0)
                    throw new EndOfStreamException();
                if (read > MaxRequestBodySize - total)
                    return false;
                total += read;
                size -= read;
            }
            await ReadChunkLineEnd(stream, buffer, cancellationToken).Vhc();
        }

        await HttpRequestHeadReader.ReadTrailers(stream, request, cancellationToken).Vhc();
        return true;
    }

    private static async Task Skip(Stream stream, byte[] buffer, long count, CancellationToken cancellationToken)
    {
        while (count > 0) {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(count, buffer.Length)),
                cancellationToken).Vhc();
            if (read == 0)
                throw new EndOfStreamException();
            count -= read;
        }
    }

    private static async Task<int> ReadChunkSize(Stream stream, byte[] buffer,
        CancellationToken cancellationToken)
    {
        uint size = 0;
        var digits = 0;
        while (true) {
            var b = await ReadByte(stream, buffer, cancellationToken).Vhc();
            var digit = b switch {
                >= (byte)'0' and <= (byte)'9' => b - '0',
                >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
                _ => -1
            };
            if (digit >= 0) {
                if (++digits > 8)
                    throw new InvalidDataException("A chunk size has more than eight digits.");
                size = (size << 4) | (uint)digit;
                continue;
            }

            if (digits == 0 || size > int.MaxValue)
                throw new InvalidDataException("Invalid chunk size.");
            // Extensions have no size limit in Kestrel. Skip them without retaining their bytes, under the
            // body's drain deadline, while still requiring the size line's CRLF.
            if (b == ';') {
                do {
                    b = await ReadByte(stream, buffer, cancellationToken).Vhc();
                    if (b == '\n')
                        throw new InvalidDataException("A chunk's line must end in CRLF.");
                } while (b != '\r');
            }
            if (b != '\r' || await ReadByte(stream, buffer, cancellationToken).Vhc() != '\n')
                throw new InvalidDataException("A chunk's line must end in CRLF.");
            return (int)size;
        }
    }

    private static async Task ReadChunkLineEnd(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        if (await ReadByte(stream, buffer, cancellationToken).Vhc() != '\r' ||
            await ReadByte(stream, buffer, cancellationToken).Vhc() != '\n')
            throw new InvalidDataException("Chunk data must end in CRLF.");
    }

    private static async ValueTask<byte> ReadByte(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        return await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).Vhc() == 0
            ? throw new EndOfStreamException()
            : buffer[0];
    }

    // -1 once the connection ends or sits idle past Kestrel's keep-alive timeout before the first byte
    private static async Task<int> ReadFirstByte(Stream stream, CancellationToken cancellationToken)
    {
        using var idleCts = new CancellationTokenSource(Heartbeat(KeepAliveTimeout));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(idleCts.Token, cancellationToken);
        var buffer = new byte[1];
        try {
            // Leading line ends do not begin a request. Keep the original idle deadline until another byte
            // arrives; otherwise a peer that sends CRLF alone gets a 408 that Kestrel never sends.
            while (true) {
                if (await stream.ReadAsync(buffer, linkedCts.Token).Vhc() == 0)
                    return -1;
                if (buffer[0] is not ((byte)'\r' or (byte)'\n'))
                    return buffer[0];
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException &&
                                   idleCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            return -1;
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
