using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.DomainFiltering.SniExtractors.Tls;

/// <summary>
/// Stream-based TLS SNI extractor for TCP connections.
/// Reads from a stream and extracts SNI from TLS ClientHello.
/// </summary>
public static class TlsSniExtractor
{
    public static async Task<TlsSniData> ExtractSni(Stream tcpStream, EventId eventId, int streamHeaderBufferSize,
        CancellationToken cancellationToken)
    {
        // extract SNI
        var initBuffer = new Memory<byte>(new byte[streamHeaderBufferSize]);
        var bufCount = await tcpStream.ReadAsync(initBuffer, cancellationToken).Vhc();
        var readData = initBuffer[..bufCount];

        return new TlsSniData {
            DomainName = ExtractSni(readData.Span, eventId),
            ReadData = readData
        };
    }

    public static string? ExtractSni(ReadOnlySpan<byte> payloadData, EventId eventId)
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