using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors.TlsStream;

/// <summary>
/// Stream-based TLS SNI extractor for TCP connections.
/// Reads from a stream and extracts SNI from TLS ClientHello.
/// </summary>
internal static class StreamSniExtractor
{
    // What the read begins with: enough for nearly every ClientHello, and what a stream that begins none is read by,
    // as its prefetch reads it. It grows to what a ClientHello's records declare, the limit at most, so a connection
    // does not pay for the largest ClientHello
    private const int InitialBufferSize = 4096;

    public static async Task<StreamSniResult> ExtractSni(Stream tcpStream, int streamHeaderBufferSize,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        // One read gives what has arrived, and a ClientHello may come in pieces, as a large post-quantum one does, and
        // in more than one TLS record, with the name in any of them: the reads go on until all its records are in, the
        // buffer is full or the stream ends. Bytes that begin no ClientHello are taken as the first read left them.
        // The reads share one time limit. A protocol in which the server speaks first, as SMTP, FTP or MySQL, sends
        // nothing until it hears the server, which is not reached before the verdict: such a stream goes on with no
        // name. A ClientHello begun whose name cannot be read, as one not complete by then, gives its error with the
        // bytes read, as its name would decide the connection
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var buffer = new byte[Math.Min(InitialBufferSize, streamHeaderBufferSize)];
        var count = 0;
        try {
            int read;
            int recordsLength;
            do {
                read = await tcpStream.ReadAsync(buffer.AsMemory(count), linkedCts.Token).Vhc();
                count += read;
                recordsLength = TlsClientHelloParser.GetRecordsLength(buffer.AsSpan(0, count));
                if (recordsLength > buffer.Length && buffer.Length < streamHeaderBufferSize)
                    Array.Resize(ref buffer,
                        Math.Min(Math.Max(recordsLength, buffer.Length * 2), streamHeaderBufferSize));
            } while (read != 0 && count < buffer.Length && count < recordsLength);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested &&
                                                 !cancellationToken.IsCancellationRequested) {
            return new StreamSniResult {
                DomainName = null,
                ReadData = buffer.AsMemory(0, count),
                Error = count > 0
                    ? new TimeoutException("A TLS ClientHello was not complete in time, so its server name is not known.")
                    : null
            };
        }

        // a ClientHello that the stream ended, that outgrew the buffer, or that a record of another type cut, before it
        // was complete: its name may be in the part never read, and the connection would pass the domain rules unnamed
        var readData = buffer.AsMemory(0, count);
        var wantedLength = TlsClientHelloParser.GetRecordsLength(readData.Span);
        if (wantedLength == TlsClientHelloParser.NeverComplete || count < wantedLength)
            return new StreamSniResult {
                DomainName = null,
                ReadData = readData,
                Error = new InvalidDataException(
                    "A TLS ClientHello could not be read to its end, so its server name is not known.")
            };

        // one that is whole and not well formed: its name is not known either. No fault of the parse is caught here:
        // read as no name, it would pass the connection unnamed
        if (!TlsClientHelloParser.TryGetServerNameFromRecords(readData.Span, out var serverName))
            return new StreamSniResult {
                DomainName = null,
                ReadData = readData,
                Error = new InvalidDataException("A TLS ClientHello is not well formed, so its server name is not known.")
            };

        return new StreamSniResult { DomainName = serverName, ReadData = readData };
    }
}
