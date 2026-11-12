using System.Net;
using System.Text;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Tunneling.Utils;

/// <summary>Provides the reading of an HTTP response's header off a stream, as a client reads its server's.</summary>
public static class HttpUtils
{
    internal const string HttpRequestKey = "HTTP_REQUEST";
    internal const int MaxHeaderSize = 128 * 1024; // 128 KB

    /// <summary>Reads an HTTP header off a stream, up to and with the blank line that ends it.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <param name="maxLength">The longest header it takes, in bytes. The default is 128 KB.</param>
    /// <returns>
    /// A task whose result is the header, at its start: empty where the stream ended before anything came.
    /// </returns>
    /// <exception cref="Exception">The stream ended inside the header, or the header is longer than allowed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    internal static async Task<MemoryStream> ReadHeadersAsync(Stream stream,
        CancellationToken cancellationToken, int maxLength = MaxHeaderSize)
    {
        // read header
        var memStream = new MemoryStream(1024);
        try {
            var readBuffer = new byte[1];
            var lfCounter = 0;
            while (lfCounter < 4) {
                var bytesRead = await stream.ReadAsync(readBuffer, 0, 1, cancellationToken).Vhc();
                if (bytesRead == 0)
                    return memStream.Length == 0
                        ? memStream // connection has been closed gracefully before sending anything
                        : throw new Exception("HttpStream has been closed unexpectedly.");

                if (readBuffer[0] == '\r' || readBuffer[0] == '\n')
                    lfCounter++;
                else
                    lfCounter = 0;

                await memStream.WriteAsync(readBuffer, 0, 1, cancellationToken).Vhc();

                if (memStream.Length > maxLength)
                    throw new Exception("HTTP header is too big.");
            }

            memStream.Position = 0;
            return memStream;
        }
        catch {
            await memStream.DisposeAsync().Vhc();
            throw;
        }
    }

    /// <summary>Reads an HTTP response's header off a stream: its status and headers, with no body.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <param name="maxLength">The longest header it takes, in bytes. The default is 128 KB.</param>
    /// <returns>A task whose result is the response.</returns>
    /// <exception cref="Exception">
    /// The stream ended, before the header or inside it; the header is longer than allowed; or it is not an HTTP
    /// response's.
    /// </exception>
    /// <exception cref="FormatException">The status code is not a number.</exception>
    /// <exception cref="OverflowException">The status code is out of an integer's range.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<HttpResponseMessage> ReadResponse(Stream stream, CancellationToken cancellationToken,
        int maxLength = MaxHeaderSize)
    {
        using var headerStream = await ReadHeadersAsync(stream, cancellationToken, maxLength).Vhc();
        using var reader = new StreamReader(headerStream, Encoding.UTF8);
        var firstLine = await reader.ReadLineAsync(cancellationToken).Vhc() ?? string.Empty; // it may return null

        // Check if the first line starts with "HTTP/" (basic HTTP format validation).
        if (!firstLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Invalid HTTP response format. Make sure you have the latest version.");

        var parts = firstLine.Split(' ');
        if (parts.Length < 2)
            throw new Exception("Invalid HTTP response. Make sure you have the latest version.");

        var response = new HttpResponseMessage((HttpStatusCode)int.Parse(parts[1]));
        while (await reader.ReadLineAsync(cancellationToken).Vhc() is { Length: > 0 } line) {
            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!response.Headers.TryAddWithoutValidation(name, value))
                response.Content.Headers.TryAddWithoutValidation(name, value);
        }
        return response;
    }


    internal static async Task<Dictionary<string, string>?> ParseHeadersAsync(Stream stream,
        CancellationToken cancellationToken, int maxLength = MaxHeaderSize) //128 KB 
    {
        using var memStream = await ReadHeadersAsync(stream, cancellationToken, maxLength).Vhc();
        if (memStream.Length == 0)
            return null; // connection has been closed gracefully

        var reader = new StreamReader(memStream, Encoding.UTF8);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Read the header lines until an empty line is encountered
        while (true) {
            // ReSharper disable once MethodHasAsyncOverload
            var line = await reader.ReadLineAsync(cancellationToken).Vhc();
            if (string.IsNullOrEmpty(line))
                break;

            if (headers.Count == 0)
                headers[HttpRequestKey] = line;

            // Split the header line into header field and value
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex > 0) {
                var headerField = line[..separatorIndex].Trim();
                var headerValue = line[(separatorIndex + 1)..].Trim();
                headers[headerField] = headerValue;
            }
        }

        // ignore start padding
        var paddingStart = headers.GetValueOrDefault("X-StartPadding");
        if (int.TryParse(paddingStart, out var paddingLength) && paddingLength > 0) 
            await stream.CopyToAsync(Stream.Null, paddingLength, cancellationToken).Vhc();

        return headers;
    }

}
