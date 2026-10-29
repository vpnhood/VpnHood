using System.Globalization;
using System.Text;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.ProbeResistance;

// the server's face to anyone who is not a client: ASP.NET Core's Kestrel behind a JWT bearer check, 401 on every
// path, and HTTPS redirection on port 80, with Kestrel's parsing, limits, timeouts and keep-alive, so a scan finds
// such a service and nothing that sets this server apart. The parity harness of the IP-blocking plan compares them
internal static class WebFront
{
    // Kestrel's defaults; its TLS handshake has its own timeout, which no heartbeat rounds
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(130);
    private static readonly TimeSpan RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestBodyDrainTimeout = TimeSpan.FromSeconds(5);
    private const long MaxRequestBodySize = 30_000_000;
    private const int MaxChunkLineSize = 4096;

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
        catch (Exception) when (headersCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
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
        await stream.WriteAsync(reply, cancellationToken).Vhc();
        await stream.FlushAsync(cancellationToken).Vhc();
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
        catch (Exception) when (drainCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            return WebFrontNext.Reset;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException) {
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
            var sizeText = (await ReadLine(stream, buffer, cancellationToken).Vhc()).Split(';')[0].Trim();
            if (sizeText.Length == 0 || !long.TryParse(sizeText, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var size) || size < 0)
                return false;

            if (size == 0)
                break;

            total += size;
            if (total > MaxRequestBodySize)
                return false;

            await Skip(stream, buffer, size, cancellationToken).Vhc();
            if ((await ReadLine(stream, buffer, cancellationToken).Vhc()).Length != 0)
                return false;
        }

        while ((await ReadLine(stream, buffer, cancellationToken).Vhc()).Length != 0) {
        }

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

    private static async Task<string> ReadLine(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        while (true) {
            if (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).Vhc() == 0)
                throw new EndOfStreamException();

            if (buffer[0] == '\n')
                return line.ToString().TrimEnd('\r');

            if (line.Length >= MaxChunkLineSize)
                throw new InvalidDataException("A chunk's line is too long.");

            line.Append((char)buffer[0]);
        }
    }

    // -1 once the connection ends or sits idle past Kestrel's keep-alive timeout before the first byte
    private static async Task<int> ReadFirstByte(Stream stream, CancellationToken cancellationToken)
    {
        using var idleCts = new CancellationTokenSource(Heartbeat(KeepAliveTimeout));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(idleCts.Token, cancellationToken);
        var buffer = new byte[1];
        try {
            return await stream.ReadAsync(buffer, linkedCts.Token).Vhc() == 0 ? -1 : buffer[0];
        }
        catch (Exception) when (idleCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
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

    private static async Task TryWrite(Stream stream, byte[] reply, CancellationToken cancellationToken)
    {
        try {
            await stream.WriteAsync(reply, cancellationToken).Vhc();
            await stream.FlushAsync(cancellationToken).Vhc();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) {
            // the connection is going anyway
        }
    }
}
