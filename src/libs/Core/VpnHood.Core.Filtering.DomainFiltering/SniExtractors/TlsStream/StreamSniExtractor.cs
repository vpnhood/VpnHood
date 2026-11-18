using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors.TlsStream;

/// <summary>
/// Stream-based TLS SNI extractor for TCP connections.
/// Reads from a stream and extracts SNI from TLS ClientHello.
/// </summary>
internal static class StreamSniExtractor
{
    private const int RecordHeaderLength = 5;

    // the first record's header and the handshake's own: the fewest bytes that tell a ClientHello and its length
    private const int ClientHelloStartLength = RecordHeaderLength + 4;

    public static async Task<StreamSniResult> ExtractSni(Stream tcpStream, EventId eventId, int streamHeaderBufferSize,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        // One read gives what has arrived, and a ClientHello may come in pieces, as a large post-quantum one does, and
        // in more than one TLS record, with the name in any of them: the reads go on until all its records are in, the
        // buffer is full or the stream ends. Bytes that begin no ClientHello are taken as the first read left them.
        // The reads share one time limit. A protocol in which the server speaks first, as SMTP, FTP or MySQL, sends
        // nothing until it hears the server, which is not reached before the verdict: such a stream goes on with no
        // name. A ClientHello begun and not complete by then fails, as its name would decide the connection
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var buffer = new byte[streamHeaderBufferSize];
        var count = 0;
        try {
            int read;
            do {
                read = await tcpStream.ReadAsync(buffer.AsMemory(count), linkedCts.Token).Vhc();
                count += read;
            } while (read != 0 && count < buffer.Length && count < GetWantedLength(buffer.AsSpan(0, count)));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested &&
                                                 !cancellationToken.IsCancellationRequested) {
            if (count > 0)
                throw new TimeoutException("A TLS ClientHello was not complete in time, so its server name is not known.");

            return new StreamSniResult { DomainName = null, ReadData = Memory<byte>.Empty };
        }

        // a ClientHello that the stream ended, or that outgrew the buffer, before it was complete: its name may be in
        // the part never read, and the connection would pass the domain rules unnamed
        var readData = buffer.AsMemory(0, count);
        if (count < GetWantedLength(readData.Span))
            throw new InvalidDataException("A TLS ClientHello ended before it was complete, so its server name is not known.");

        return new StreamSniResult {
            DomainName = TryExtractSni(readData.Span, eventId),
            ReadData = readData
        };
    }

    // How many bytes to have before the name is looked for: all the records that hold a ClientHello, as far as the
    // bytes read tell, or what is there already when they begin no ClientHello
    private static int GetWantedLength(ReadOnlySpan<byte> data)
    {
        if (!IsClientHelloStart(data))
            return data.Length;

        // too few bytes to tell its length yet
        if (data.Length < ClientHelloStartLength)
            return ClientHelloStartLength;

        // the handshake's length, from its header at the start of the first record's fragment, then the records until
        // their fragments hold it all
        var handshakeLength = 4 + ((data[6] << 16) | (data[7] << 8) | data[8]);
        var position = 0;
        var collected = 0;
        while (true) {
            if (data.Length < position + RecordHeaderLength)
                return position + RecordHeaderLength;

            // a record of another type ends what a handshake can be read from: its parse finds no name
            if (data[position] != 0x16)
                return data.Length;

            var recordLength = (data[position + 3] << 8) | data[position + 4];
            var recordEnd = position + RecordHeaderLength + recordLength;
            collected += recordLength;
            if (collected >= handshakeLength || data.Length < recordEnd)
                return recordEnd;

            position = recordEnd;
        }
    }

    // Whether the bytes, as far as they go, are the start of a TLS handshake record that holds a ClientHello: type
    // 0x16, version 3.x, then the handshake's type 0x01. Strict, as the reads wait for the rest of what it finds
    private static bool IsClientHelloStart(ReadOnlySpan<byte> data)
    {
        return data.Length > 0 && data[0] == 0x16 &&
               (data.Length < 2 || data[1] == 0x03) &&
               (data.Length < 3 || data[2] <= 0x04) &&
               (data.Length < 6 || data[5] == 0x01);
    }

    private static string? TryExtractSni(ReadOnlySpan<byte> data, EventId eventId)
    {
        if (!IsClientHelloStart(data))
            return null;

        try {
            return TlsClientHelloParser.ExtractSni(JoinHandshakeFragments(data), hasRecordHeader: false);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(eventId, ex, "Could not extract sni.");
            return null;
        }
    }

    // the handshake's bytes, from the fragments of the handshake records that hold it
    private static byte[] JoinHandshakeFragments(ReadOnlySpan<byte> data)
    {
        var handshake = new List<byte>(data.Length);
        var position = 0;
        while (data.Length >= position + RecordHeaderLength && data[position] == 0x16) {
            var recordLength = (data[position + 3] << 8) | data[position + 4];
            var start = position + RecordHeaderLength;
            handshake.AddRange(data.Slice(start, Math.Min(recordLength, data.Length - start)));
            position = start + recordLength;
        }

        return [.. handshake];
    }
}
