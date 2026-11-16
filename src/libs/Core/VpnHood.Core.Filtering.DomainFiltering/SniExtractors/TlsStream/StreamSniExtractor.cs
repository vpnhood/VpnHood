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
    // the record's header and the handshake's type: the fewest bytes that tell a ClientHello
    private const int ClientHelloStartLength = 6;

    public static async Task<StreamSniResult> ExtractSni(Stream tcpStream, EventId eventId, int streamHeaderBufferSize,
        CancellationToken cancellationToken)
    {
        // One read gives what has arrived, and a ClientHello may come in pieces, as a large post-quantum one does,
        // with the name in any of them: the reads go on until its record is in, the buffer is full or the stream
        // ends. Bytes that begin no ClientHello are taken as the first read left them
        var buffer = new byte[streamHeaderBufferSize];
        var count = 0;
        while (true) {
            var read = await tcpStream.ReadAsync(buffer.AsMemory(count), cancellationToken).Vhc();
            count += read;
            if (read == 0 || count >= GetWantedLength(buffer.AsSpan(0, count), buffer.Length))
                break;
        }

        var readData = buffer.AsMemory(0, count);
        return new StreamSniResult {
            DomainName = TryExtractSni(readData.Span, eventId),
            ReadData = readData
        };
    }

    // How many bytes to have before the name is looked for: a ClientHello's whole record, as far as the buffer holds
    // it, or what is there already when the bytes begin no ClientHello
    private static int GetWantedLength(ReadOnlySpan<byte> data, int capacity)
    {
        if (!IsClientHelloStart(data))
            return data.Length;

        // too few bytes to tell yet
        if (data.Length < ClientHelloStartLength)
            return Math.Min(ClientHelloStartLength, capacity);

        var recordLength = (data[3] << 8) | data[4];
        return Math.Min(5 + recordLength, capacity);
    }

    // Whether the bytes, as far as they go, are the start of a TLS handshake record that holds a ClientHello: type
    // 0x16, version 3.x, then the handshake's type 0x01. Strict, as the reads wait for the rest of what it finds
    private static bool IsClientHelloStart(ReadOnlySpan<byte> data)
    {
        return data.Length > 0 && data[0] == 0x16 &&
               (data.Length < 2 || data[1] == 0x03) &&
               (data.Length < 3 || data[2] <= 0x04) &&
               (data.Length < ClientHelloStartLength || data[5] == 0x01);
    }

    private static string? TryExtractSni(ReadOnlySpan<byte> payloadData, EventId eventId)
    {
        try {
            return TlsClientHelloParser.ExtractSni(payloadData, hasRecordHeader: true);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(eventId, ex, "Could not extract sni.");
            return null;
        }
    }
}