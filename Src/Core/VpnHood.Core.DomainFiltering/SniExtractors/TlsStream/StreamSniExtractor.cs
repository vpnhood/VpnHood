using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.DomainFiltering.SniExtractors.TlsStream;

/// <summary>
/// Stream-based TLS SNI extractor for TCP connections.
/// Reads from a stream and extracts SNI from TLS ClientHello.
/// </summary>
public static class StreamSniExtractor
{
    public static async Task<StreamSniResult> ExtractSni(Stream tcpStream, EventId eventId, int streamHeaderBufferSize,
        CancellationToken cancellationToken)
    {
        // extract SNI
        var initBuffer = new Memory<byte>(new byte[streamHeaderBufferSize]);
        var bufCount = await tcpStream.ReadAsync(initBuffer, cancellationToken).Vhc();
        var readData = initBuffer[..bufCount];

        return new StreamSniResult {
            DomainName = TryExtractSni(readData.Span, eventId),
            ReadData = readData
        };
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